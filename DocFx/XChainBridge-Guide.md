# Cross-Chain Bridge Guide (XLS-38d)

This guide explains how to use XRP Ledger Cross-Chain Bridges with the XrplCSharp SDK. Cross-chain bridges enable transferring value (XRP or IOU tokens) between two XRPL chains (a locking chain and an issuing chain).

## Table of Contents

- [Overview](#overview)
- [Key Concepts](#key-concepts)
- [Bridge Types](#bridge-types)
- [Transaction Types](#transaction-types)
- [Step-by-Step: XRP-XRP Bridge](#step-by-step-xrp-xrp-bridge)
- [Step-by-Step: IOU-IOU Bridge](#step-by-step-iou-iou-bridge)
- [Witness Server and Attestations](#witness-server-and-attestations)
- [Ledger Objects](#ledger-objects)
- [Common Errors](#common-errors)
- [Best Practices](#best-practices)

---

## Overview

A cross-chain bridge connects two XRPL chains:

- **Locking Chain** — the chain where value is locked (held in escrow by a door account)
- **Issuing Chain** — the chain where equivalent value is issued (minted by a door account)

When a user transfers value from the locking chain to the issuing chain, the original value is locked on the locking chain, and a wrapped equivalent is issued on the issuing chain. The reverse process burns the wrapped value and unlocks the original.

### Architecture

```
Locking Chain                           Issuing Chain
┌──────────────────┐                   ┌──────────────────┐
│                  │                   │                  │
│  User ──► Door   │   Attestations   │   Door ──► User  │
│  (lock value)    │ ◄──────────────► │  (issue value)   │
│                  │   Witness Server  │                  │
└──────────────────┘                   └──────────────────┘
```

---

## Key Concepts

### Door Accounts

Each bridge has two **door accounts** — one on each chain. The door account is the custodian that holds locked value (locking side) or issues/burns wrapped value (issuing side).

### Bridge Definition (`XChainBridgeModel`)

A bridge is uniquely identified by four fields that must be **exactly identical** across all transactions referencing the same bridge:

```csharp
using Xrpl.Models.Common;
using static Xrpl.Models.Common.Common;

var bridge = new XChainBridgeModel
{
    LockingChainDoor = "rLockingDoorAddress",
    LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
    IssuingChainDoor = "rIssuingDoorAddress",
    IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
};
```

> **Critical:** Any mismatch in the bridge definition between transactions will cause failures. The bridge object must be bit-for-bit identical in every transaction that references it.

### Witness Servers

Witness servers monitor both chains and provide **attestations** — cryptographic proofs that a transaction occurred on one chain, enabling the corresponding action on the other chain. A bridge requires one or more witness servers configured as signers on the door accounts.

### Claim IDs

A **Claim ID** is a unique identifier allocated before a cross-chain transfer. It tracks the transfer and ensures attestations are correctly associated.

### Signature Reward

The `SignatureReward` is an XRP amount paid to witness servers for providing attestations. It is **always denominated in XRP**, regardless of the bridge type (XRP or IOU).

---

## Bridge Types

### XRP-XRP Bridge

Transfers native XRP between chains.

**Rules:**
- `LockingChainIssue` and `IssuingChainIssue` must both be `{"currency": "XRP"}`
- `IssuingChainDoor` **must be the genesis account** on the issuing chain (`rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh` for standalone/testnet)

```csharp
var bridge = new XChainBridgeModel
{
    LockingChainDoor = walletDoor.ClassicAddress,
    LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
    IssuingChainDoor = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
    IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
};
```

### IOU-IOU Bridge

Transfers issued tokens (IOU) between chains.

**Rules:**
- `LockingChainIssue` and `IssuingChainIssue` specify the token with `currency` and `issuer`
- On the locking side, door and issuer **can be different** accounts
- On the issuing side, **`IssuingChainDoor` must equal `IssuingChainIssue.issuer`** — the door account IS the token issuer
- The locking door needs a TrustLine to the locking issuer
- The locking issuer must have `DefaultRipple` enabled if third-party transfers are needed (e.g., XChainCommit)

```csharp
var bridge = new XChainBridgeModel
{
    LockingChainDoor = walletLockingDoor.ClassicAddress,
    LockingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletLockingIssuer.ClassicAddress
    },
    IssuingChainDoor = walletIssuingDoor.ClassicAddress,
    IssuingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletIssuingDoor.ClassicAddress  // MUST equal IssuingChainDoor
    },
};
```

---

## Transaction Types

| Transaction | Purpose | Who Submits |
|------------|---------|-------------|
| `XChainCreateBridge` | Create a new bridge | Door account |
| `XChainModifyBridge` | Update SignatureReward or MinAccountCreateAmount | Door account |
| `XChainCreateClaimID` | Allocate a claim ID for a transfer | Any account |
| `XChainCommit` | Lock value on the source chain | User |
| `XChainClaim` | Claim value on the destination chain | User (with attestations) |
| `XChainAccountCreateCommit` | Create a new account on the destination chain | User |
| `XChainAddClaimAttestation` | Submit witness attestation for a commit | Witness server |
| `XChainAddAccountCreateAttestation` | Submit witness attestation for account creation | Witness server |

---

## Step-by-Step: XRP-XRP Bridge

### 1. Create the Bridge

The door account submits `XChainCreateBridge` to register the bridge on the ledger:

```csharp
using Xrpl.Models.Transactions;
using Xrpl.Models.Common;
using Xrpl.Sugar;

XChainCreateBridge createBridge = new XChainCreateBridge
{
    Account = walletDoor.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },       // 100 drops
    MinAccountCreateAmount = new Currency { Value = "10000000", CurrencyCode = "XRP" }, // 10 XRP
};
createBridge = await client.Autofill(createBridge);
TransactionSummary result = await client.SubmitAndWait(createBridge, walletDoor, true);
```

- `SignatureReward` — XRP drops paid to witnesses per attestation
- `MinAccountCreateAmount` — minimum XRP for `XChainAccountCreateCommit` (optional)

### 2. Create a Claim ID

Before transferring value, the user must allocate a claim ID:

```csharp
XChainCreateClaimID createClaimId = new XChainCreateClaimID
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
    OtherChainSource = walletUser.ClassicAddress,  // source account on the other chain
};
createClaimId = await client.Autofill(createClaimId);
TransactionSummary result = await client.SubmitAndWait(createClaimId, walletUser, true);
```

### 3. Commit Value (Lock on Source Chain)

The user commits XRP to the bridge. This locks the value on the locking chain:

```csharp
XChainCommit commit = new XChainCommit
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",     // the claim ID from step 2
    Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },  // 1 XRP
    OtherChainDestination = destinationAddress,  // optional: destination on the other chain
};
commit = await client.Autofill(commit);
TransactionSummary result = await client.SubmitAndWait(commit, walletUser, true);
```

### 4. Witness Attestation

Witness servers observe the commit on the locking chain and submit attestations on the issuing chain:

```csharp
XChainAddClaimAttestation attestation = new XChainAddClaimAttestation
{
    Account = witnessAccount.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",
    Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
    OtherChainSource = walletUser.ClassicAddress,
    AttestationSignerAccount = witnessAccount.ClassicAddress,
    AttestationRewardAccount = witnessAccount.ClassicAddress,
    PublicKey = witnessPublicKeyHex,
    Signature = attestationSignatureHex,
    WasLockingChainSend = 1,  // 1 = locking chain, 0 = issuing chain
    Destination = destinationAddress,
};
attestation = await client.Autofill(attestation);
TransactionSummary result = await client.SubmitAndWait(attestation, witnessAccount, true);
```

### 5. Claim Value (Receive on Destination Chain)

Once sufficient attestations are collected, the user claims value on the issuing chain:

```csharp
XChainClaim claim = new XChainClaim
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",
    Destination = walletUser.ClassicAddress,
    Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
};
claim = await client.Autofill(claim);
TransactionSummary result = await client.SubmitAndWait(claim, walletUser, true);
```

> **Note:** If the commit included `OtherChainDestination` and sufficient attestations are received, the value may be automatically delivered without an explicit `XChainClaim`.

### 6. Modify Bridge (Optional)

The door account can update the bridge parameters:

```csharp
XChainModifyBridge modify = new XChainModifyBridge
{
    Account = walletDoor.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "200", CurrencyCode = "XRP" },
};
modify = await client.Autofill(modify);
TransactionSummary result = await client.SubmitAndWait(modify, walletDoor, true);
```

### 7. Create Account on Destination Chain (Optional)

To create a new account on the destination chain via the bridge:

```csharp
XChainAccountCreateCommit accountCreate = new XChainAccountCreateCommit
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    Destination = newAccountAddress,
    Amount = new Currency { Value = "20000000", CurrencyCode = "XRP" },     // 20 XRP
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
};
accountCreate = await client.Autofill(accountCreate);
TransactionSummary result = await client.SubmitAndWait(accountCreate, walletUser, true);
```

The bridge must have `MinAccountCreateAmount` set, and the `Amount` must be >= `MinAccountCreateAmount`.

---

## Step-by-Step: IOU-IOU Bridge

IOU bridges require additional setup compared to XRP bridges.

### Prerequisites

#### 1. Enable DefaultRipple on the Locking Issuer

The issuer must allow rippling between third-party accounts:

```csharp
using Xrpl.Models.Transactions;

AccountSet enableRipple = new AccountSet
{
    Account = walletLockingIssuer.ClassicAddress,
    SetFlag = AccountSetAsfFlags.asfDefaultRipple,
};
enableRipple = await client.Autofill(enableRipple);
await client.SubmitAndWait(enableRipple, walletLockingIssuer, true);
```

> **Important:** `DefaultRipple` must be enabled **before** creating TrustLines. TrustLines inherit the NoRipple state from the issuer's `DefaultRipple` flag at creation time.

#### 2. Create TrustLines

The locking door needs a TrustLine to the locking issuer:

```csharp
TrustSet trustSet = new TrustSet
{
    Account = walletLockingDoor.ClassicAddress,
    LimitAmount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "10000000",
    }
};
trustSet = await client.Autofill(trustSet);
await client.SubmitAndWait(trustSet, walletLockingDoor, true);
```

If users will commit IOU tokens, they also need TrustLines:

```csharp
TrustSet userTrust = new TrustSet
{
    Account = walletUser.ClassicAddress,
    LimitAmount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "10000000",
    }
};
userTrust = await client.Autofill(userTrust);
await client.SubmitAndWait(userTrust, walletUser, true);
```

> **Note:** The issuing door does **not** need a TrustLine to itself — it IS the token issuer on the issuing chain.

#### 3. Issue Tokens to Users

Before users can commit IOU tokens, they need a balance:

```csharp
Payment issueTokens = new Payment
{
    Account = walletLockingIssuer.ClassicAddress,
    Destination = walletUser.ClassicAddress,
    Amount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "1000",
    },
};
issueTokens = await client.Autofill(issueTokens);
await client.SubmitAndWait(issueTokens, walletLockingIssuer, true);
```

### Create the IOU Bridge

```csharp
XChainBridgeModel bridge = new XChainBridgeModel
{
    LockingChainDoor = walletLockingDoor.ClassicAddress,
    LockingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletLockingIssuer.ClassicAddress
    },
    IssuingChainDoor = walletIssuingDoor.ClassicAddress,
    IssuingChainIssue = new IssuedCurrency
    {
        Currency = "USD",
        Issuer = walletIssuingDoor.ClassicAddress  // MUST equal IssuingChainDoor
    },
};

XChainCreateBridge createBridge = new XChainCreateBridge
{
    Account = walletLockingDoor.ClassicAddress,
    XChainBridge = bridge,
    SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
};
createBridge = await client.Autofill(createBridge);
await client.SubmitAndWait(createBridge, walletLockingDoor, true);
```

### Commit IOU Tokens

The flow is similar to XRP, but the `Amount` is an IOU object:

```csharp
XChainCommit commit = new XChainCommit
{
    Account = walletUser.ClassicAddress,
    XChainBridge = bridge,
    XChainClaimID = "1",
    Amount = new Currency
    {
        CurrencyCode = "USD",
        Issuer = walletLockingIssuer.ClassicAddress,
        Value = "100",
    },
    OtherChainDestination = destinationAddress,
};
commit = await client.Autofill(commit);
await client.SubmitAndWait(commit, walletUser, true);
```

---

## Witness Server and Attestations

Witness servers are essential for cross-chain bridges. They:

1. Monitor transactions on both chains
2. Verify that commits/account creates occurred
3. Submit attestation transactions on the other chain

### Signer List Setup

Door accounts must configure a `SignerList` that includes the witness server accounts. The quorum determines how many attestations are required.

### Attestation Transactions

| Transaction | Attests to |
|------------|-----------|
| `XChainAddClaimAttestation` | An `XChainCommit` on the other chain |
| `XChainAddAccountCreateAttestation` | An `XChainAccountCreateCommit` on the other chain |

### Key Fields

| Field | Description |
|-------|-------------|
| `AttestationSignerAccount` | The witness account (must be on the door's signer list) |
| `AttestationRewardAccount` | Account that receives the signature reward share |
| `PublicKey` | Hex-encoded public key of the witness |
| `Signature` | Hex-encoded attestation signature |
| `WasLockingChainSend` | `1` if the attested event was on the locking chain, `0` if on the issuing chain |

---

## Ledger Objects

Bridges create the following ledger objects:

| Object | Description | Created By |
|--------|-------------|------------|
| `Bridge` | The bridge definition, owned by the door account | `XChainCreateBridge` |
| `XChainOwnedClaimID` | A claim ID for tracking a transfer | `XChainCreateClaimID` |
| `XChainOwnedCreateAccountClaimID` | Tracks an account creation transfer | `XChainAccountCreateCommit` |

### Querying Bridge State

Use `account_objects` to retrieve bridge objects owned by a door account:

```csharp
using Xrpl.Models.Methods;

var request = new AccountObjectsRequest(walletDoor.ClassicAddress);
var response = await client.AccountObjects(request);

foreach (var obj in response.AccountObjectList)
{
    Console.WriteLine($"Type: {obj.LedgerEntryType}");
}
```

---

## Common Errors

| Error Code | Cause | Solution |
|-----------|-------|---------|
| `temXCHAIN_BRIDGE_BAD_ISSUES` | Invalid bridge definition | Verify all four bridge fields. For XRP bridges: IssuingChainDoor must be genesis. For IOU bridges: IssuingChainDoor must equal IssuingChainIssue.issuer |
| `tecXCHAIN_NO_CLAIM_ID` | Claim ID does not exist | Create a claim ID before committing |
| `tecNO_PERMISSION` | Account is not the bridge door | Only the door account can create/modify bridges |
| `terNO_RIPPLE` | Rippling not enabled between accounts | Enable `DefaultRipple` on the IOU issuer before creating TrustLines |
| `tecUNFUNDED` | Insufficient balance | Ensure the committing account has sufficient funds |
| `tecXCHAIN_BAD_CLAIM_ID` | Wrong claim ID | Verify the claim ID matches an existing one |

### `temXCHAIN_BRIDGE_BAD_ISSUES` Checklist

This is the most common error. Verify:

1. **XRP bridge:** `IssuingChainDoor` = genesis account (`rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh`)
2. **XRP bridge:** Both `LockingChainIssue` and `IssuingChainIssue` = `{"currency": "XRP"}` (no issuer field)
3. **IOU bridge:** `IssuingChainDoor` == `IssuingChainIssue.Issuer`
4. **IOU bridge:** Both issue fields have `currency` AND `issuer`
5. **All bridges:** The `XChainBridge` object is **exactly identical** across all transactions

### `terNO_RIPPLE` Checklist

1. Call `AccountSet` with `SetFlag = AccountSetAsfFlags.asfDefaultRipple` on the issuer
2. Do this **before** creating TrustLines (TrustLines inherit the flag at creation time)
3. Verify TrustLines do not have `NoRipple` set explicitly

---

## Best Practices

1. **Store the bridge definition once** — create the `XChainBridgeModel` object once and reuse it across all transactions. Any difference in the bridge fields will cause failures.

2. **Use constants for door addresses** — especially the genesis account for XRP bridges:
   ```csharp
   const string GenesisAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
   ```

3. **Enable DefaultRipple early** — for IOU bridges, enable it on the issuer before any TrustLine creation.

4. **SignatureReward is always XRP** — regardless of whether the bridge transfers XRP or IOU tokens.

5. **MinAccountCreateAmount** — set this only on XRP bridges where cross-chain account creation is needed.

6. **Validate results** — always check `TransactionResult` for `tesSUCCESS`:
   ```csharp
   if (result.Meta?.TransactionResult != "tesSUCCESS")
       throw new Exception($"Transaction failed: {result.Meta?.TransactionResult}");
   ```

7. **Witness server security** — in production, use a multi-signature signer list with a quorum > 1. Never rely on a single witness.

8. **Amount format:**
   - XRP: `new Currency { Value = "1000000", CurrencyCode = "XRP" }` (value in drops)
   - IOU: `new Currency { CurrencyCode = "USD", Issuer = "rAddress", Value = "100" }` (decimal value)

9. **Testing on standalone** — XChainBridge amendment must be enabled. Use `feature` RPC command to unveto it if needed:
   ```json
   { "command": "feature", "feature": "XChainBridge", "vetoed": false }
   ```

---

## Related Resources

- [XLS-38d Specification](https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0038d-cross-chain-bridge)
- [XRPL Documentation: Cross-Chain Bridges](https://xrpl.org/docs/concepts/interoperability/cross-chain-bridges)
- [XChainBridge Transaction Reference](https://xrpl.org/docs/references/protocol/transactions/types/xchaincreatebridge)
