# Amounts, Quality and Offer Crossing

This guide covers the amount arithmetic of the XRP Ledger in the XrplCSharp SDK. `XrplAmount` holds an amount the way rippled's `STAmount` does. `XrplAmountMath` repeats the node's arithmetic on it, including the directed rounding used when offers cross. `XrplQuality` is the exchange rate of an offer, and `OfferCrossing` sizes what one offer contributes to a payment or an `OfferCreate`. `OfferCreateCrossing` crosses a whole `OfferCreate` against the order books and AMM pools it reaches, and `PaymentFlow` runs a `Payment` along its paths. `DexQuoteSugar` quotes both against a node's ledger, and `PathFinding` finds a payment's paths as `ripple_path_find` does.

Every result matches rippled 3.4.0 bit for bit: mantissa, exponent and sign. The tests replay 8,808 vectors produced by rippled's own code, rebuild the offer crossings of rippled's `AMM_test.cpp`, and compare offer crossings with a live node.

## Table of Contents

- [Overview](#overview)
- [XrplAmount](#xrplamount)
- [Arithmetic](#arithmetic)
- [Quality](#quality)
- [Offer Crossing](#offer-crossing)
- [Crossing an OfferCreate](#crossing-an-offercreate)
- [Evaluating a Payment](#evaluating-a-payment)
- [Permissioned Domains and Credentials](#permissioned-domains-and-credentials)
- [Reading Books from a Node](#reading-books-from-a-node)
- [Quotes](#quotes)
- [Finding Paths](#finding-paths)
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
| `PaymentFlow` | `Xrpl.Amounts` | What a `Payment` does along its paths, from a `DexSnapshot` |
| `DexQuoteSugar` | `Xrpl.Sugar` | Quotes for a payment or an offer, read and computed against a node's validated ledger |
| `PathFinding` | `Xrpl.Amounts` | A payment's paths, found as `ripple_path_find` finds them, over a snapshot or a node |

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
- **Checks.** Given the SDK's `OfferCreate` model, the crossing first runs the offer's own checks: the malformed offers of `preflight` (`temINVALID_FLAG`, `temBAD_EXPIRATION`, `temBAD_SEQUENCE`, `temBAD_OFFER`, `temREDUNDANT`, `temBAD_CURRENCY`), then `preclaim`'s `tecFROZEN`, `tecUNFUNDED_OFFER`, `tecEXPIRED`, `tecNO_ISSUER`, and `tecNO_LINE` or `tecNO_AUTH` when the issuer of what the account asks for requires authorization. `OfferSequence` cancels the account's older offer before anything crosses.
- **Authorization.** An offer whose owner the issuer no longer lets hold what the offer asks for is removed, not crossed.
- **Trust lines.** A line the crossing creates is owned by the account that receives on it. A line whose balance returns to zero, with the sending side back at its defaults, stops counting toward that account's reserve, and is deleted when neither side counts it. `TrustLines` lists both kinds.

```csharp
LedgerRules rules = await LedgerRules.FromNodeAsync(client);
OfferCreate offer = await client.Autofill(new OfferCreate
{
    Account = account,
    TakerPays = takerPays.ToCurrency(),     // what the account wants
    TakerGets = takerGets.ToCurrency(),     // what it gives
    Flags = OfferCreateFlags.tfImmediateOrCancel,
});

DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, offer);
OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, offer, rules);

// result.EngineResult and result.Applied: the code, and whether the fee is charged
// result.BalanceChanges: every account's change, as the metadata will record it
// result.Offers: the offers crossed or removed, and what is left of each
// result.Pools: the pools' balances afterwards
// result.TrustLines: the trust lines created or deleted
// result.PlacedTakerPays / PlacedTakerGets: the offer left in the book, if any
```

`OfferCreateCrossing.Cross(snapshot, account, takerPays, takerGets, fee, flags, rules)` takes the same offer as separate values, without `OfferSequence`, `Expiration` or a domain.

`DexSnapshot.FromNodeAsync` reads everything at one validated ledger: the books the crossing can reach, the pools on them, the accounts and trust lines it reads, and the offer `OfferSequence` cancels. [Reading Books from a Node](#reading-books-from-a-node) explains how deep. A snapshot can also be built by hand, as the unit tests do.

The result is exact against the snapshot. The transaction lands in a later ledger, whose state can differ, so `simulate` stays the reference before submitting. Not covered: MPT books and sponsored reserves. A payment is evaluated by [`PaymentFlow`](#evaluating-a-payment).

## Evaluating a Payment

`PaymentFlow.Evaluate` runs a `Payment` against a `DexSnapshot` the way rippled 3.4.0 applies it. It takes the SDK's own `Payment` model, with its `Fee`, `SendMax`, `DeliverMin`, `Paths` and flags.

- **Checks.** The malformed payments of `preflight` (`temBAD_AMOUNT`, `temREDUNDANT`, the `temBAD_SEND_XRP_*` codes, `temMALFORMED` for a bad `CredentialIDs` or `DomainID`), the destination checks of `preclaim` (`tecNO_DST`, `tecNO_DST_INSUF_XRP`, `tecDST_TAG_NEEDED`), the credentials presented and the domain (see [Permissioned Domains and Credentials](#permissioned-domains-and-credentials)), then deposit authorization.
- **Strands.** Each path, and the default path unless `tfNoDirectRipple` is set, becomes a strand: the source, SendMax's issuer, the path, a book to the delivered asset and its issuer, the destination. A malformed or looping path is refused with the node's `temBAD_PATH` or `temBAD_PATH_LOOP`.
- **Trust lines.** Each step along a line reads who owes whom, the line's `QualityIn` and `QualityOut`, its limit, NoRipple, freezes and authorization. The transfer fee applies where the payment redeems into an issuer and it issues onward.
- **Books and pools.** They are walked as for an `OfferCreate`, except that the offer's owner does not pay the fee on what it gives: the next step charges it to the sender.
- **The result.** `SendMax`, `DeliverMin`, `tfPartialPayment` and `tfLimitQuality` decide how much flows and the result code: `tecPATH_PARTIAL`, `tecPATH_DRY`, or `tesSUCCESS` with the delivered amount. Trust lines are created and deleted as for an `OfferCreate`.

```csharp
Payment payment = await client.Autofill(new Payment
{
    Account = sender,
    Destination = receiver,
    Amount = new Currency { CurrencyCode = "EUR", Issuer = eurIssuer, Value = "20" },
    SendMax = new Currency { CurrencyCode = "USD", Issuer = usdIssuer, Value = "25" },
    Paths = alternative.PathsComputed,      // from ripple_path_find
});

DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, payment);
PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, payment, rules);

// result.EngineResult and result.Applied: the code, and whether the fee is charged
// result.DeliveredAmount: delivered_amount
// result.Paid: what the sender spent, fees included
// result.BalanceChanges, result.Offers, result.Pools: what the payment changed
```

`DexSnapshot.FromNodeAsync(client, payment)` reads, at one validated ledger, the trust lines along every path the payment can take, the books and pools on them, the accounts, the destination's preauthorizations, the credentials the payment presents, and its domain. The paths come with the payment: from `ripple_path_find`, or from the local [path finder](#finding-paths). Not covered: MPT payments and sponsored reserves.

## Permissioned Domains and Credentials

An `OfferCreate` or a `Payment` with a `DomainID` trades in that permissioned domain:

- **Membership.** The account - and a payment's destination - must be in the domain: its owner, or the holder of a credential the domain accepts, accepted and not expired. Otherwise the result is `tecNO_PERMISSION`; a member whose only matching credential has expired gets `tecEXPIRED`.
- **Books.** The strands walk the domain's books instead of the open ones: the domain's offers and the hybrid offers (`tfHybrid`), which sit in the open book as well. An AMM pool never serves a domain book. An offer whose owner has left the domain - its credential expired - is removed from the domain's book on the way.
- **Credentials.** A payment's `CredentialIDs` must name credentials that exist, belong to the sender and were accepted (`tecBAD_CREDENTIALS`), and have not expired (`tecEXPIRED`). A destination with deposit authorization lets the payment through when the credentials, as a set of issuer and type, are one it preauthorized.

`DexSnapshot.FromNodeAsync` reads the domain, the domain's books, and the credentials the domain accepts for the accounts involved and for every owner of a domain offer. A snapshot built by hand lists them in `Domains` and `Credentials`, and marks each domain offer with its `DomainId` and `Hybrid`.

## Reading Books from a Node

By default `DexSnapshot.FromNodeAsync` reads each book with one `book_offers` request. That gives the funded offers at the top of the book: at most 100 from a public node, up to 400 over an admin connection. A book that returns a full page is listed in `DexSnapshot.PartialBooks`, and a result whose engine walks past the last offer read of such a book says so with `NeedsDeeperBooks`: the node may cross offers the snapshot does not have.

`DexSnapshotOptions.BookDepth` reads deeper. The loader walks the book's directories the way the engine does - quality by quality, page by page - and reads up to that many offers from each book, unfunded ones included, so the offers the node removes on the way appear in the result as well:

```csharp
DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(client, offer, new DexSnapshotOptions { BookDepth = 1_000 });
OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, offer, rules);
if (result.NeedsDeeperBooks)
{
    // More than 1,000 offers of a book were reached: read deeper, or treat the result as a bound.
}
```

A walk costs one `ledger_entry` request per offer, so it is worth it for the books a transaction actually reaches deep into.

## Quotes

`DexQuoteSugar` reads the snapshot and runs the engine in one call, and reads deeper by itself while a result `NeedsDeeperBooks`:

```csharp
// What the offer or the payment would do if it were applied now.
OfferCrossingResult crossing = await client.QuoteOfferCreateAsync(offer);
PaymentFlowResult payment = await client.QuotePaymentAsync(existingPayment);

// Deliver exactly 20 USD: the cost in XRP, along the paths ripple_path_find returns.
PaymentQuote deliver = await client.QuoteDeliverAsync(sender, receiver, XrplAmount.Parse(usd, "20"), xrp);
// deliver.Cost: what the sender spends; deliver.Payment: Amount, SendMax = Cost, Paths

// Spend exactly 10 XRP: how much USD it delivers.
PaymentQuote spend = await client.QuoteSpendAsync(sender, receiver, XrplAmount.Parse(xrp, "10000000"), usd);
// spend.Delivered: what the receiver gets; spend.Payment: Amount = Delivered, SendMax = 10 XRP, Paths
```

A `PaymentQuote.Payment` is ready to autofill, sign and submit: against an unchanged ledger it delivers exactly what was quoted. To leave room for the market to move, raise its `SendMax`, or add `tfPartialPayment` with a `DeliverMin`.


## Finding Paths

`PathFinding.FindAsync` finds a payment's paths the way rippled 3.4.0's `ripple_path_find` does: its `PathRequest` and `Pathfinder` - the table of path shapes for each kind of payment, accounts and books added link by link, candidate accounts ranked by the ways they lead on, every complete path run through the payment engine and ranked by quality, liquidity and length - then the cost of the best ones.

A search runs over a `PathfindingSource`: a `DexSnapshot`, or a node read at one ledger. It also needs the order books, which the node keeps in an index of its own and a client supplies as an `IBookIndex`:

| `BookIndex` factory | What it holds | When to use it |
|---|---|---|
| `FromLedgerAsync` | Every book directory and AMM pool of a ledger, as rippled's `OrderBookDB` holds them; `Observe(meta)` adds the books later transactions create | A private node, a standalone one, a test network: the same paths as the node |
| `FromAssetsAsync` | The books between the assets given, found with `book_offers` and `amm_info` | Mainnet, when the assets that matter are known |
| `FromAccountsAsync` | The books between the assets the accounts' trust lines hold or issue | Mainnet, as a guess from the source and the destination |
| `FromSnapshot` | The books of a snapshot's offers and pools | A snapshot built by hand |

```csharp
BookIndex books = await BookIndex.FromLedgerAsync(client);
PathfindingSource source = await PathfindingSource.FromNodeAsync(client, books);
PathFindResult result = await PathFinding.FindAsync(source, new PathFindRequest
{
    SourceAccount = sender,
    DestinationAccount = receiver,
    DestinationAmount = XrplAmount.Parse(usd, "20"),   // "-1" asks for as much as SendMax buys
    SourceCurrencies = new[] { xrp },                   // every asset the sender holds when empty
    SearchLevel = 2,                                    // the node's default; 7 is its slow search
}, rules);

foreach (PathFindAlternative alternative in result.Alternatives)
{
    // alternative.SourceAmount: what the payment costs from that asset
    // alternative.PathsComputed: the payment's Paths
}

// Or in one call, with the books between the two accounts' assets:
PathFindResult found = await client.FindPathsAsync(request);
```

Against the same ledger and the same books, every alternative - its cost, what it delivers, its paths in order - is the node's. The node computes a search outside any transaction, where the checks rippled reads from the current transaction's rules - the AMM's `fixAMMv1_1` and `fixAMMv1_3` rounding, `fixReducedOffersV2` - see their amendments as disabled; the local search does the same. A payment built from a search can therefore deliver a last digit less than the search said, on the node as locally. The order of the alternatives, and of books of equal worth, follows the node's hash sets and may differ.

`QuoteDeliverAsync` and `QuoteSpendAsync` take a `books` argument too: with one, their paths come from the local search instead of `ripple_path_find`.

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
