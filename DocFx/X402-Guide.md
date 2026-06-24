# x402 Agentic Payments Guide

`Xrpl.X402` and `Xrpl.X402.AspNetCore` add [x402](https://github.com/x402-foundation/x402) (HTTP-402) agentic payments to the XRP Ledger, implementing the t54 **"XRPL exact scheme"**. They let an AI agent / client autonomously pay for an HTTP resource (an API, model inference, a service) in **XRP** or **RLUSD/IOU**, and let a server require payment for an endpoint.

| Package | Role |
|---|---|
| [Xrpl.X402](reference/Xrpl.X402.html) | **Client** — a `DelegatingHandler` that detects HTTP 402, builds and locally signs an XRPL `Payment`, and retries with a `PAYMENT-SIGNATURE` header. |
| [Xrpl.X402.AspNetCore](reference/Xrpl.X402.AspNetCore.html) | **Server** — a `RequirePayment` endpoint filter + facilitators that verify and settle the payment on-ledger. |

The client **signs but does not submit** — the merchant's facilitator settles the transaction (per the t54 scheme). Live interop with the t54 testnet facilitator is verified on-chain for both XRP and RLUSD/IOU.

## Wire format (t54 XRPL exact scheme)

| Header | Direction | Body (base64-encoded JSON) |
|---|---|---|
| `PAYMENT-REQUIRED` | server → client (on 402) | `{ x402Version, accepts: [ {scheme:"exact", network, asset, payTo, amount, maxTimeoutSeconds, extra} ] }` |
| `PAYMENT-SIGNATURE` | client → server | `{ x402Version:2, accepted, payload:{ signedTxBlob, invoiceId } }` |
| `PAYMENT-RESPONSE` | server → client | `{ success, transaction, network, payer }` |

- `asset` is `"XRP"` (amount in **drops**) or a 40-hex currency code (e.g. RLUSD) with `extra.issuer` and a decimal `amount`.
- The payment intent is bound to the XRPL transaction via the native `InvoiceID` field (`Payment.InvoiceID = SHA-256(invoiceId)`) and/or a `Memo` (`MemoData` = hex of the `invoiceId` string), selectable via [`X402IntentBinding`](reference/Xrpl.X402.X402IntentBinding.html) (default `Both`).
- t54 enforces a `SourceTag` — the requirement's `extra.sourceTag` (the x402 protocol value `804681468`) is stamped on the transaction.

## Client usage

```csharp
using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;

IXrplClient xrpl = /* your connected IXrplClient */;
XrplWallet wallet = XrplWallet.FromSeed(seed);

// The signer autofills + signs locally (it never submits).
var signer = new XrplWalletX402Signer(xrpl, wallet);

var options = new X402ClientOptions
{
    Network = "xrpl:1",            // CAIP-2 network you will pay on
    MaxAmountDrops = 10_000_000,   // hard cap for XRP payments (10 XRP)
    // IouValueCaps["rIssuer..."] = 5m,  // REQUIRED to pay an IOU/RLUSD from that issuer
};

// Wrap any HttpClient with the payment handler.
var http = new HttpClient(new X402PaymentHandler(signer, options)
{
    InnerHandler = new HttpClientHandler()
});

// Transparent: a 402 is paid automatically and the resource is returned.
HttpResponseMessage resource = await http.GetAsync("https://api.example.com/paid");
```

### Security model (the merchant's 402 is treated as untrusted)

- **Spending caps enforced *before* signing.** XRP is capped by `MaxAmountDrops` (always checked). **IOU/RLUSD fails closed**: refused unless an explicit per-issuer cap exists in `IouValueCaps`.
- **Optional `PayToAllowlist`** — when non-empty, both `payTo` and the IOU `issuer` must be allow-listed.
- **Anti-double-pay** — the client pays at most once per request; a repeated 402 throws.
- **Validity window** — `LastLedgerSequence` is capped by the requirement's `maxTimeoutSeconds`.
- All refusals throw [`X402PaymentException`](reference/Xrpl.X402.X402PaymentException.html) with a machine-readable `Reason`.

## End-to-end flow (XRP)

The exchange is three HTTP steps. `X402PaymentHandler` performs steps 2–3 transparently inside a single `GetAsync`; below is what actually travels on the wire.

### 1. Client requests the resource — server quotes a price

```http
GET /paid
→ 402 Payment Required
  PAYMENT-REQUIRED: <base64>
```

Decoded `PAYMENT-REQUIRED`:

```json
{
  "x402Version": 2,
  "accepts": [{
    "scheme": "exact",
    "network": "xrpl:1",
    "asset": "XRP",
    "payTo": "rMerchant...",
    "amount": "1000000",
    "maxTimeoutSeconds": 600,
    "extra": { "invoiceId": "inv-123", "sourceTag": 804681468 }
  }]
}
```

### 2. Client pays — retries with a signed payment

The handler selects the `exact` requirement on its configured network, enforces the spending caps, builds and locally signs an XRPL `Payment` (1 XRP to `rMerchant`, `InvoiceID = SHA-256("inv-123")`, `SourceTag = 804681468`), then retries with the `PAYMENT-SIGNATURE` header:

```http
GET /paid
  PAYMENT-SIGNATURE: <base64>
```

Decoded `PAYMENT-SIGNATURE`:

```json
{
  "x402Version": 2,
  "accepted": { "scheme": "exact", "network": "xrpl:1", "asset": "XRP", "payTo": "rMerchant...", "amount": "1000000", "maxTimeoutSeconds": 600, "extra": { "invoiceId": "inv-123", "sourceTag": 804681468 } },
  "payload": { "signedTxBlob": "1200002280000000...", "invoiceId": "inv-123" }
}
```

### 3. Server settles — returns the resource + a receipt

The facilitator verifies the signed payment and settles it on-ledger (waits for `tesSUCCESS`), then serves the resource with a `PAYMENT-RESPONSE` receipt:

```http
200 OK
  PAYMENT-RESPONSE: <base64>

premium content
```

Decoded `PAYMENT-RESPONSE`:

```json
{ "success": true, "transaction": "8DB5B4144A24E7D72FED584D8B0EAFFE19B9034FE5EC3DD296B19FED5731B7E8", "network": "xrpl:1", "payer": "rPayer..." }
```

### Reading the receipt on the client

`GetAsync` returns the final `200` response; decode the `PAYMENT-RESPONSE` header to get the settlement result and on-ledger transaction hash:

```csharp
using System.Linq;
using Xrpl.X402.Wire;

HttpResponseMessage r = await http.GetAsync("https://api.example.com/paid");
string content = await r.Content.ReadAsStringAsync();   // "premium content"

if (r.Headers.TryGetValues(X402Headers.PaymentResponse, out var values))
{
    PaymentResponseEnvelope receipt = X402Base64Json.Decode<PaymentResponseEnvelope>(values.First());
    Console.WriteLine($"paid: {receipt.Success}, tx: {receipt.Transaction}, payer: {receipt.Payer}");
}
```

## Paying in RLUSD / IOU

The flow is identical — only the requirement differs, and the payer must already hold the token and a trust line to the issuer. The server quotes an issued currency:

```json
{
  "scheme": "exact",
  "network": "xrpl:1",
  "asset": "524C555344000000000000000000000000000000",
  "payTo": "rMerchant...",
  "amount": "2.50",
  "maxTimeoutSeconds": 600,
  "extra": {
    "invoiceId": "inv-456",
    "issuer": "rIssuer...",
    "sourceTag": 804681468
  }
}
```

- `asset` is the 40-hex currency code (e.g. RLUSD), `amount` is a decimal string, and `extra.issuer` is **required**.
- The client builds an issued-currency `Payment` whose `Amount` is `{ currency, issuer, value }` and adds a matching `SendMax`; steps 2–3 and the receipt are exactly as above.
- An IOU payment **fails closed** unless a per-issuer cap is configured:

```csharp
var options = new X402ClientOptions { Network = "xrpl:1" };
options.IouValueCaps["rIssuer..."] = 10m;   // allow up to 10.0 of this issuer's token
// var http = new HttpClient(new X402PaymentHandler(signer, options) { InnerHandler = new HttpClientHandler() });
```

## Server usage (`Xrpl.X402.AspNetCore`)

```csharp
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

IXrplClient xrpl = /* your connected IXrplClient */;
IX402Facilitator facilitator = new LedgerSettlingFacilitator(xrpl);
// or: new T54Facilitator(new HttpClient());  // delegate to a t54 facilitator

app.MapGet("/paid", () => "premium content")
   .RequirePayment(facilitator, ctx => new PaymentRequirement
   {
       Scheme = "exact", Network = "xrpl:1", Asset = "XRP",
       PayTo = "rYourMerchantAddress...",
       Amount = "1000000",            // 1 XRP in drops
       MaxTimeoutSeconds = 600,
       Extra =
       {
           ["invoiceId"] = JsonSerializer.SerializeToElement("inv-123"),
           ["sourceTag"] = JsonSerializer.SerializeToElement(804681468)
       }
   });
```

Without a `PAYMENT-SIGNATURE` the endpoint returns **402** + `PAYMENT-REQUIRED`. With a valid signature, the facilitator verifies the destination, settles the transaction (waits for `tesSUCCESS`), sets `PAYMENT-RESPONSE`, and runs your handler.

Two [`IX402Facilitator`](reference/Xrpl.X402.AspNetCore.IX402Facilitator.html) implementations ship:

- [`LedgerSettlingFacilitator`](reference/Xrpl.X402.AspNetCore.LedgerSettlingFacilitator.html) — settles **locally** against your own connected node.
- [`T54Facilitator`](reference/Xrpl.X402.AspNetCore.T54Facilitator.html) — delegates verify + settle to an **external t54 facilitator** over HTTP.

## Verifiable Intent

A passthrough is provided: set `X402ClientOptions.VerifiableIntentProvider` (an [`IVerifiableIntentProvider`](reference/Xrpl.X402.IVerifiableIntentProvider.html)) and its `extensions` object is attached to each PAYMENT-SIGNATURE under `x402Secure.verifiableIntentChain`. The full SD-JWT L1→L3 credential chain (Mastercard Agentic / Trustline) is supplied by your own provider — this package does not generate it.

## Status

- XRP and RLUSD/IOU supported. Only the `exact` scheme is implemented (the only scheme t54 advertises for XRPL).
- Live t54 interop confirmed on testnet for both XRP and RLUSD/IOU (`/verify` → `isValid:true`, `/settle` settles on-chain).

See the [Xrpl.X402 API reference](reference/Xrpl.X402.html) and [Xrpl.X402.AspNetCore API reference](reference/Xrpl.X402.AspNetCore.html) for full type documentation.
