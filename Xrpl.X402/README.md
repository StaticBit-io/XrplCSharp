# Xrpl.X402

> **Preview.** x402 (HTTP-402) agentic payments for the XRP Ledger, implementing the t54 **"XRPL exact scheme"** wire format. Lets an AI agent / client autonomously pay for HTTP resources (APIs, model inference, services) in **XRP** or **RLUSD/IOU**, and lets a server require payment for an endpoint.

Two packages:

| Package | Role |
|---|---|
| `Xrpl.X402` | **Client** — a `DelegatingHandler` that detects HTTP 402, builds + locally signs an XRPL `Payment`, and retries with a `PAYMENT-SIGNATURE` header. |
| `Xrpl.X402.AspNetCore` | **Server** — a `RequirePayment` endpoint filter + a ledger-settling facilitator that returns 402, verifies the signed payment, and settles it on-ledger. |

Both build on the [`Xrpl`](https://www.nuget.org/packages/Xrpl) SDK. The client **signs but does not submit** — the merchant's facilitator settles the transaction (per the t54 scheme).

---

## Wire format (t54 XRPL exact scheme)

| Header | Direction | Body (base64-encoded JSON) |
|---|---|---|
| `PAYMENT-REQUIRED` | server → client (on 402) | `{ x402Version, accepts: [ {scheme:"exact", network, asset, payTo, amount, maxTimeoutSeconds, extra} ] }` |
| `PAYMENT-SIGNATURE` | client → server | `{ x402Version:2, accepted, payload:{ signedTxBlob } }` |
| `PAYMENT-RESPONSE` | server → client | `{ success, transaction, network, payer }` |

- `asset` is `"XRP"` (amount in **drops**) or a 40-hex currency code (e.g. RLUSD) with `extra.issuer` and a decimal `amount`.
- The payment intent is bound to the XRPL transaction via a **Memo** (`MemoType=hex("x402")`, `MemoFormat=hex("application/json")`, `MemoData=hex(JSON{paymentId, sessionId?})`), **not** the `InvoiceID` field.
- Default x402 `SourceTag` = `804681468`. `DestinationTag` is intentionally not set.

---

## Client usage

```csharp
using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;

// A connected XRPL client + a wallet you control.
IXrplClient xrpl = /* your IXrplClient, connected */;
XrplWallet wallet = XrplWallet.FromSeed(seed);

// The signer autofills + signs locally (it never submits).
var signer = new XrplWalletX402Signer(xrpl, wallet);

var options = new X402ClientOptions
{
    Network = "xrpl:1",            // CAIP-2 network you will pay on
    MaxAmountDrops = 10_000_000,   // hard cap for XRP payments (10 XRP)
    // IouValueCaps["rIssuer..."] = 5m,   // REQUIRED to pay an IOU/RLUSD from that issuer
    // PayToAllowlist = { "rMerchant...", "rIssuer..." },  // optional allowlist
};

// Wrap any HttpClient with the payment handler.
var http = new HttpClient(new X402PaymentHandler(signer, options)
{
    InnerHandler = new HttpClientHandler()
});

// Transparent: a 402 is paid automatically and the resource is returned.
HttpResponseMessage resource = await http.GetAsync("https://api.example.com/paid");
```

### Security model (the client treats the merchant's 402 as untrusted)

- **Spending caps enforced *before* signing.** XRP is capped by `MaxAmountDrops` (always checked). **IOU/RLUSD fails closed**: a payment is refused unless an explicit per-issuer cap exists in `IouValueCaps`.
- **Optional `PayToAllowlist`** — when non-empty, both `payTo` and the IOU `issuer` must be allow-listed.
- **Anti-double-pay** — the client pays at most once per request; a repeated 402 throws.
- **Validity window** — `LastLedgerSequence` is capped by the requirement's `maxTimeoutSeconds`.
- All refusals throw `X402PaymentException` (`Reason` carries a machine code, e.g. `amount_over_cap`, `no_acceptable_requirement`, `payto_not_allowed`, `issuer_not_allowed`, `payment_rejected`).

---

## Server usage (`Xrpl.X402.AspNetCore`)

```csharp
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

IXrplClient xrpl = /* your connected IXrplClient */;
IX402Facilitator facilitator = new LedgerSettlingFacilitator(xrpl);

app.MapGet("/paid", () => "premium content")
   .RequirePayment(facilitator, ctx => new PaymentRequirement
   {
       Scheme = "exact",
       Network = "xrpl:1",
       Asset = "XRP",
       PayTo = "rYourMerchantAddress...",
       Amount = "1000000",            // 1 XRP in drops
       MaxTimeoutSeconds = 60,
       Extra = { ["invoiceId"] = JsonSerializer.SerializeToElement("inv-123") }
   });
```

Without a `PAYMENT-SIGNATURE` the endpoint returns **402** + `PAYMENT-REQUIRED`. With a valid signature, `LedgerSettlingFacilitator` verifies the destination, settles the transaction (`SubmitRequestAndWait`, waits for `tesSUCCESS`), sets `PAYMENT-RESPONSE`, and runs your handler. Implement `IX402Facilitator` yourself to delegate settlement to an external facilitator (e.g. t54) instead of settling locally.

---

## Interop notes / configurability

Different x402 implementations name the payment id differently (t54 requirement uses `extra.invoiceId`; the conformant `mpcp-protocol/x402-xrpl` memo uses `paymentId`). These names are configurable on `X402ClientOptions`:

| Option | Default | Meaning |
|---|---|---|
| `InvoiceIdExtraKey` | `"invoiceId"` | key in the requirement's `extra` to read the payment id from |
| `SessionIdExtraKey` | `"sessionId"` | key in `extra` for the optional session id |
| `MemoPaymentIdField` | `"paymentId"` | JSON field name for the payment id inside the memo |
| `MemoSessionIdField` | `"sessionId"` | JSON field name for the session id inside the memo |

## Status & limitations (preview)

- XRP and RLUSD/IOU are supported. The exact wire format has been verified against the t54 docs and the `mpcp-protocol/x402-xrpl` conformance adapter, and exercised end-to-end against a standalone `rippled`. It has **not yet been pinned against a live t54 facilitator** — verify the payment-id field naming (above) against your target facilitator before production use.
- Settlement model follows t54 (client signs, facilitator settles). Multi-scheme support and a verifiable-intent layer are out of scope for this preview.

## References

- t54 XRPL x402 scheme: <https://xrpl-x402.t54.ai/docs/xrpl-scheme>
- x402 protocol: <https://github.com/x402-foundation/x402>
- XRPL agentic transactions: <https://xrpl.org/docs/agents/agentic-transactions>
