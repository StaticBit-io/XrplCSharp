# Sponsored Fees & Reserves Guide (XLS-68)

This guide explains how to use XRPL Sponsored Fees & Reserves with the XrplCSharp SDK. Sponsorship lets one account (the **sponsor**) pay transaction fees and/or object reserves on behalf of another account (the **sponsee**) — the building block for gasless onboarding and custodial UX.

> **Note:** Requires the `Sponsor` amendment (XLS-68). As of mid-2026 it exists only on the rippled `develop` branch — it is not part of any release and is not active on mainnet or testnet. Use the nightly Docker stand from this repository to try it (see [Testing](#testing)). The feature is in draft and subject to change.

## Table of Contents

- [Overview](#overview)
- [Key Concepts](#key-concepts)
- [Transaction Types](#transaction-types)
- [Step-by-Step: Establishing a Sponsorship](#step-by-step-establishing-a-sponsorship)
- [Sending a Sponsored Transaction](#sending-a-sponsored-transaction)
- [Signing Flows (V1/V2/V3)](#signing-flows-v1v2v3)
- [Fees](#fees)
- [Ledger Objects](#ledger-objects)
- [Testing](#testing)
- [Common Errors](#common-errors)

---

## Overview

```
Sponsor (rSponsor...)                    Sponsee (rUser...)
┌───────────────────────┐               ┌───────────────────────┐
│ SponsorshipSet        │──────────────►│ may now send txs with │
│   Sponsee: rUser...   │               │   Sponsor: rSponsor...│
│   FeeAmount: 5 XRP    │               │   SponsorFlags: fee   │
│   RemainingOwnerCount │               │ fee is charged to the │
└───────────────────────┘               │ sponsor, not the user │
        creates                         └───────────────────────┘
┌───────────────────────┐
│ Sponsorship (ledger)  │
│   Owner / Sponsee     │
│   FeeAmount budget    │
│   RemainingOwnerCount │
└───────────────────────┘
```

Two independent dimensions can be sponsored:

| Dimension | `SponsorFlags` value | What the sponsor pays |
|---|---|---|
| Fees | `SponsorCoverage.spfSponsorFee` (= 1) | The `Fee` of the sponsee's transactions |
| Reserves | `SponsorCoverage.spfSponsorReserve` (= 2) | Owner reserves of objects the sponsee creates |

Every transaction type gains three common fields: `Sponsor`, `SponsorFlags`, and (when the sponsorship demands a co-signature) `SponsorSignature` — an inner not-signing STObject carrying the sponsor's `SigningPubKey` + `TxnSignature` over the **same preimage** as the main signature.

---

## Key Concepts

### The Sponsorship ledger object

Created by `SponsorshipSet`, one per sponsor/sponsee pair:

| Field | Meaning |
|---|---|
| `Owner` | The sponsoring account |
| `Sponsee` | The sponsored account |
| `FeeAmount` | Remaining XRP budget for sponsored fees |
| `RemainingOwnerCount` | How many more objects the sponsor will cover reserves for |

### Require-signature mode

By default a sponsee can spend the sponsorship budget without the sponsor's participation. `SponsorshipSet` flags flip that per dimension:

- `tfSponsorshipSetRequireSignForFee` / `tfSponsorshipClearRequireSignForFee`
- `tfSponsorshipSetRequireSignForReserve` / `tfSponsorshipClearRequireSignForReserve`

When required, the sponsee's transaction must carry a valid `SponsorSignature` — see [Signing Flows](#signing-flows-v1v2v3).

### Accounting fields

- `AccountRoot`: `SponsoredOwnerCount`, `SponsoringOwnerCount`, `SponsoringAccountCount`
- `RippleState`: `HighSponsor` / `LowSponsor` — who covers the reserve of each trust-line side

---

## Transaction Types

### SponsorshipSet (sent by the sponsor)

Creates/updates the relationship or deletes it with `tfDeleteObject`:

```csharp
var setup = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmount = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCount = 3,
};
setup = await client.Autofill(setup);
await client.SubmitAndWait(setup, sponsor, true);
```

### SponsorshipTransfer

Moves or terminates an existing sponsorship. Exactly one mode flag is required:

| Flag | Who sends | Extra fields |
|---|---|---|
| `tfSponsorshipCreate` | current sponsor | `Sponsor` (the new sponsor) required, `Sponsee` forbidden |
| `tfSponsorshipReassign` | current sponsor | `Sponsor` required, `Sponsee` forbidden |
| `tfSponsorshipEnd` | sponsee or sponsor | `Sponsor` forbidden; `Sponsee` must differ from `Account` |

---

## Step-by-Step: Establishing a Sponsorship

```csharp
XrplWallet sponsor = XrplWallet.Generate();
XrplWallet sponsee = XrplWallet.Generate();
// fund both wallets first

var tx = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmount = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCount = 3,
};
tx = await client.Autofill(tx);
TransactionSummary result = await client.SubmitAndWait(tx, sponsor, true);
// result.Meta.TransactionResult == "tesSUCCESS"
```

Verify via `account_objects`:

```csharp
var request = new AccountObjectsRequest(sponsor.ClassicAddress) { Type = LedgerEntryType.Sponsorship };
var objects = await client.AccountObjects(request);
LOSponsorship sponsorship = objects.AccountObjectList.OfType<LOSponsorship>().First();
```

---

## Sending a Sponsored Transaction

The sponsee sends any ordinary transaction with the sponsorship fields:

```csharp
var payment = new Payment
{
    Account = sponsee.ClassicAddress,
    Destination = destination.ClassicAddress,
    Amount = new Currency { ValueAsXrp = 1m },
    Sponsor = sponsor.ClassicAddress,
    SponsorFlags = SponsorCoverage.spfSponsorFee,
};
payment = await client.Autofill(payment);
```

If the sponsorship does **not** require a co-signature, sign and submit as usual. If it does, use one of the flows below.

---

## Signing Flows (V1/V2/V3)

`SponsorSignature` is signed over the same preimage as the main signature (analogous to the LoanSet counterparty pattern). Three flows via `SponsorSigningHelper`:

**V1 — Automatic (both keys available in one process):**

```csharp
JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponseeWallet);
SignatureResult signed = SponsorSigningHelper.SignSponsored(prepared, sponseeWallet, sponsorWallet);
await client.SubmitRequest(signed.TxBlob, true);
```

**V2 — Parallel (keys on separate devices, combine later):**

```csharp
var sponsorSig  = sponsorWallet.SignAsSponsor(prepared);
var submitterSig = sponseeWallet.Sign(prepared);
var combined = SponsorSigningHelper.CombineSponsorSignatures(submitterSig.TxBlob, sponsorSig.TxBlob);
```

**V3 — Sequential (sponsor first, then submitter finalizes):**

```csharp
var withSponsor = sponsorWallet.SignAsSponsor(prepared);
var final = SponsorSigningHelper.SubmitterSign(withSponsor.TxBlob, sponseeWallet);
```

---

## Fees

Per rippled `Transactor::calculateBaseFee`, a **single-signed** `SponsorSignature` adds nothing to the base fee. Only when the sponsor co-signs with **multisig** does each signer nested in `SponsorSignature.Signers` add one base fee unit. The SDK's `Autofill` accounts for this automatically.

---

## Ledger Objects

- `LOSponsorship` — the relationship object (see [Key Concepts](#key-concepts))
- `LOAccountRoot.SponsoredOwnerCount` / `SponsoringOwnerCount` / `SponsoringAccountCount`
- `LORippleState.HighSponsor` / `LowSponsor`

---

## Testing

The amendment is develop-only. The repository ships a nightly stand with `Sponsor` enabled at genesis:

```bash
docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestISponsorship"
```

Integration tests are gated by `AmendmentGuard`: on a node without the amendment they exit as *inconclusive* instead of failing. See `Tests/Xrpl.Tests/Integration/transactions/TestISponsorship.cs` for complete working examples, and the [Standalone Node Guide](StandaloneNode-Guide.md) for stand details.

---

## Common Errors

| Error | Meaning |
|---|---|
| `temDISABLED` | The `Sponsor` amendment is not active on the node |
| `tecNO_SPONSOR_PERMISSION` | The sponsorship does not cover this dimension, is out of budget, or the required `SponsorSignature` is missing/invalid |
| `terNO_PERMISSION` | Relationship state does not allow the operation (e.g. transferring someone else's sponsorship) |
| Validation: "invalid Sponsor" | The `Sponsor` field is not a string address (client-side check) |

*Русская версия: [Sponsorship-Guide.ru](Sponsorship-Guide.ru.md)*
