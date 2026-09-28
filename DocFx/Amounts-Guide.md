# Amounts, Quality and Offer Crossing

This guide covers the amount arithmetic of the XRP Ledger in the XrplCSharp SDK. `XrplAmount` holds an amount the way rippled's `STAmount` does. `XrplAmountMath` repeats the node's arithmetic on it, including the directed rounding used when offers cross. `XrplQuality` is the exchange rate of an offer, and `OfferCrossing` sizes what one offer contributes to a payment or an `OfferCreate`. `OfferCreateCrossing` crosses a whole `OfferCreate` against the order books and AMM pools it reaches.

Every result matches rippled 3.4.0 bit for bit: mantissa, exponent and sign. The tests replay 8,808 vectors produced by rippled's own code, rebuild the offer crossings of rippled's `AMM_test.cpp`, and compare offer crossings with a live node.

## Table of Contents

- [Overview](#overview)
- [XrplAmount](#xrplamount)
- [Arithmetic](#arithmetic)
- [Quality](#quality)
- [Offer Crossing](#offer-crossing)
- [Crossing an OfferCreate](#crossing-an-offercreate)
- [Balance Changes Beyond decimal](#balance-changes-beyond-decimal)
- [Ledger Rules](#ledger-rules)

---

## Overview

`Currency` is the wire model: it keeps an amount as text. Its `ValueAsNumber` is a `decimal`, which holds about 28 digits between `1e-28` and `7.9e28`.

An issued currency on the ledger has 16 significant digits and an exponent from -96 to 80. Its range therefore runs from `1e-81` to about `1e96`. Code that has to handle any amount the ledger allows, or round the way the node rounds, computes with `XrplAmount` instead.

| Type | Namespace | Purpose |
|------|-----------|---------|
| `XrplAmount` | `Xrpl.Amounts` | An amount of one asset, over the ledger's whole range |
| `AmountKind` | `Xrpl.Amounts` | `Xrp`, `Iou` or `Mpt`, which decides how an amount rounds |
| `XrplAmountMath` | `Xrpl.Amounts` | `STAmount` arithmetic: add, multiply, divide, `mulRound`, `divRound` |
| `XrplQuality` | `Xrpl.Amounts` | The 64-bit exchange rate the ledger sorts order books by |
| `OfferCrossing` | `Xrpl.Amounts` | What one offer contributes when it is crossed, as `BookStep` sizes it |
| `OfferCreateCrossing` | `Xrpl.Amounts` | What an `OfferCreate` does against whole books and AMM pools, from a `DexSnapshot` |

## XrplAmount

An `XrplAmount` is always canonical. Each kind has its own form:

| Kind | Form |
|------|------|
| XRP | A whole number of drops, at most 10^17 |
| MPT | A whole number of units, at most 2^63 - 1 |
| Issued currency | A mantissa in [10^15, 10^16) and an exponent in [-96, 80] |

A value below that range is zero. A value above it throws `OverflowException`.

```csharp
IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = "rIssuer..." };

XrplAmount big = XrplAmount.Parse(usd, "5e90");          // far beyond decimal
XrplAmount drops = XrplAmount.Parse(new IssuedCurrency { Currency = "XRP" }, "1500000");

string text = big.ToString();                           // "5000000000000000e75", as rippled writes it
XrplAmount fromWire = someCurrency.ToXrplAmount();       // from the wire model
Currency toWire = big.ToCurrency();                      // back to it
```

`ToString` writes what rippled's `getText` writes:
- XRP and MPT as an integer;
- an issued currency in plain decimal, or in scientific notation when its exponent is below -25 or above -5.

`FromNumber` turns an `XrplNumber` into an amount of the asset. It rounds the magnitude in the given mode, then puts the sign back, as `STAmount::fromNumber` does.

## Arithmetic

`XrplAmountMath` repeats the node's operations:

| Method | rippled |
|--------|---------|
| `Add`, `Subtract` | `STAmount + STAmount` |
| `Multiply(amount, fraction, rounding)` | `multiply(amount, frac, rm)` |
| `Multiply(v1, v2, asset)` | `multiply(v1, v2, asset)` |
| `Divide(num, den, asset)` | `divide(num, den, asset)` |
| `MulRound`, `DivRound` | `mulRound`, `divRound` |
| `MulRoundStrict`, `DivRoundStrict` | `mulRoundStrict`, `divRoundStrict` |

`MulRound` and `DivRound` round away from zero when `roundUp` is true, and toward zero otherwise. The strict forms differ from the legacy ones in two places:

- **An integral result (XRP, MPT).** `MulRoundStrict` counts every discarded digit when it decides to round up. The legacy form counts only the last one.
- **Building the final amount.** The strict forms fix the rounding mode: toward zero for a product, and away from or toward zero, as asked, for a quotient. The legacy forms use the ambient mode, which on a node is to-nearest. A legacy `MulRound` asked to round down can therefore round an issued currency to nearest.

```csharp
XrplAmount paid = XrplAmountMath.MulRoundStrict(limit, rate, xrp, roundUp: true);
```

## Quality

`XrplQuality` is rippled's `Quality`: the rate `in / out` of an offer. It is encoded in 64 bits: the exponent plus 100 in the top byte, and the 16-digit mantissa below it. A lower encoding is a better quality, because the taker gets more for less. The comparison operators follow rippled's: `a < b` means `a` is the worse quality.

```csharp
XrplQuality fromAmounts = XrplQuality.FromAmounts(takerPays, takerGets);
XrplQuality fromBook = XrplQuality.FromBookDirectory(offer.BookDirectory);

// An offer from book_offers or account_objects:
XrplQuality? crossedAt = offer.BookQuality;         // the quality the node crosses it at
XrplQuality remaining = offer.RemainingQuality;     // the ratio of what it has left
```

The node crosses an offer at the quality of its book directory. That quality is set when the offer is placed, and it is not the ratio of what the offer has left.

`CeilIn`, `CeilOut`, `CeilInStrict` and `CeilOutStrict` cut an offer's amounts to an input or output limit at the quality, as `Quality::ceilIn` and `Quality::ceilOut` do.

## Offer Crossing

`OfferCrossing` sizes one offer the way rippled's `BookStep` does:

1. **`Fund`** starts from the offer. It adds the input transfer fee and applies the output transfer fee, which the owner pays. When the owner holds less than the offer gives, it cuts the offer to what they hold.
2. **`LimitStepOut`** cuts the step to an output limit. The reverse pass uses it.
3. **`LimitStepIn`** cuts the step to an input limit. The forward pass uses it.
4. **`ForwardPass`** runs the forward pass over one offer. When the result delivers more than the reverse pass asked for on no more input, it settles on the reverse pass's output, as the node does.
5. **`Cross`** runs both passes for a taker who asks for `deliver` and pays at most `sendMax`.

`LimitIn` and `LimitOut` are `TOffer::limitIn` and `limitOut`. `LimitIn` switches on `fixReducedOffersV2`. `MulRatio` is the transfer-rate multiplication of the typed amounts. `PassesQualityLimit` is the check a strand's result must pass against the taker's limit quality.

```csharp
LedgerRules rules = await LedgerRules.FromNodeAsync(client);
XrplQuality quality = XrplQuality.FromBookDirectory(makerOffer.BookDirectory);

OfferStep funded = OfferCrossing.Fund(
    quality,
    makerOffer.TakerPays.ToXrplAmount(),
    makerOffer.TakerGets.ToXrplAmount(),
    ownerFunds: makerBalance,                       // null when the owner issues what it sells
    transferRateOut: issuerTransferRate,            // OfferCrossing.QualityOne for none
    rules: rules);

OfferStep step = OfferCrossing.Cross(funded, deliver: wanted, sendMax: willingToPay, rules);
// step.StepOut: what the taker receives
// step.StepIn: what the taker pays
// step.OwnerGives: what leaves the maker, transfer fee included
// step.OfferIn / step.OfferOut: how much of the offer is consumed
```

This sizes one offer. To cross a whole `OfferCreate`, use [`OfferCreateCrossing`](#crossing-an-offercreate).

## Crossing an OfferCreate

`OfferCreateCrossing.Cross` runs an `OfferCreate` the way rippled 3.4.0's payment engine runs it, against a `DexSnapshot` of the ledger:

- **Strands.** The engine crosses the direct book. When neither side is XRP, it also crosses a bridge through XRP - two books in a row - and on each pass takes the strand with the better quality.
- **Book walk.** Each pass takes the offers of one quality level, in book order. On the way it removes offers that are expired, empty, deep-frozen, unfunded, or too small to keep their quality. Each owner pays from what it still holds after the transaction's earlier transfers, since funds received during a transaction cannot be spent in it.
- **AMM pool.** The pool is tried before the book at each pass. On a single strand, its offer is sized so the pool's price meets the book's best quality. With two strands, it offers slices that grow along the Fibonacci sequence, for at most 30 passes.
- **Limits.** The taker's price, `tfPassive`, `tfSell`, `tfImmediateOrCancel` and `tfFillOrKill` apply, and so does the issuer's tick size.

```csharp
LedgerRules rules = await LedgerRules.FromNodeAsync(client);
DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, account, takerPays.Asset, takerGets.Asset);

OfferCrossingResult result = OfferCreateCrossing.Cross(
    snapshot,
    account,
    takerPays,                              // what the account wants
    takerGets,                              // what it gives
    fee: 12,                                // the transaction's fee, in drops
    OfferCreateFlags.tfImmediateOrCancel,
    rules);

// result.EngineResult: tesSUCCESS, tecKILLED, tecUNFUNDED_OFFER or tecINSUF_RESERVE_OFFER
// result.BalanceChanges: every account's change, as the metadata will record it
// result.Offers: the offers crossed or removed, and what is left of each
// result.Pools: the pools' balances afterwards
// result.PlacedTakerPays / PlacedTakerGets: the offer left in the book, if any
```

`DexSnapshot.FromNodeAsync` reads everything at one validated ledger: the books the crossing can reach, the pools on them, and the accounts and trust lines it reads. Each book is read with one `book_offers` call, so a crossing deeper than the first 400 offers of a book is not represented. A snapshot can also be built by hand, as the unit tests do.

The result is exact against the snapshot. The transaction lands in a later ledger, whose state can differ, so `simulate` stays the reference before submitting. Not covered: permissioned-DEX domains, MPT books, `RequireAuth`, the transaction's `Expiration` and `OfferSequence`, and sponsored reserves. Payments are not covered either; their full engine is tracked in [#240](https://github.com/StaticBit-io/XrplCSharp/issues/240).

## Balance Changes Beyond decimal

`BalanceChanges.GetBalanceChanges` computes issued-currency deltas as `XrplAmount`. Each difference is exact, then rounded once to 16 significant digits, to nearest with ties to even, as rippled rounds. A balance far beyond `decimal` is reported rather than refused, and it is written in scientific notation the way rippled writes it:

```csharp
Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(result.Meta);
XrplAmount received = changes[holder].Single().ToXrplAmount();   // exact at any size
```

## Ledger Rules

The arithmetic depends on the amendments in force:
- the `Number` scale, selected by `SingleAssetVault` / `LendingProtocol`, `fixCleanup3_2_0` and `fixCleanup3_3_0`;
- `MPTokensV2`, under which an MPT result goes through `Number` arithmetic;
- `fixReducedOffersV2`, which selects `ceilInStrict` in `LimitIn`;
- `fixAMMv1_1`, `fixAMMv1_2` and `fixFillOrKill`, which change how `OfferCreateCrossing` sizes pool offers and treats `tfFillOrKill`.

`LedgerRules.FromNodeAsync(client)` reads them from the node. Pass what it returns to get the node's own results. When no rules are passed, a method uses `new LedgerRules()`: every amendment above enabled except `MPTokensV2`, whatever the node actually runs. That matches a ledger with all of them enabled, and may not match yours.
