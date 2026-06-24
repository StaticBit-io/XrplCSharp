# Xrpl.X402.AspNetCore

> **Server-side** of [x402](https://www.nuget.org/packages/Xrpl.X402) (HTTP-402) agentic payments for the XRP Ledger. Protect an ASP.NET Core endpoint so it requires an XRPL payment (XRP or RLUSD/IOU) before serving — the t54 "XRPL exact scheme".

Pairs with the client package [`Xrpl.X402`](https://www.nuget.org/packages/Xrpl.X402). Builds on the [`Xrpl`](https://www.nuget.org/packages/Xrpl) SDK.

📖 **Full documentation** — end-to-end flow (price → pay → receipt) with XRP and RLUSD/IOU examples: **[x402 Agentic Payments Guide](https://staticbit-io.github.io/XrplCSharp/X402-Guide.html)** · [API reference](https://staticbit-io.github.io/XrplCSharp/).

## What it adds

| Type | Role |
|---|---|
| `RequirePayment(...)` endpoint filter | Returns **402** + `PAYMENT-REQUIRED` when no payment is presented; with a valid `PAYMENT-SIGNATURE` it verifies + settles, sets `PAYMENT-RESPONSE`, and runs your handler. |
| `IX402Facilitator` | Abstraction that verifies a signed payment and settles it on-ledger. |
| `LedgerSettlingFacilitator(IXrplClient)` | Settles **locally** against your own connected node (`SubmitRequestAndWait`, waits for `tesSUCCESS`). |
| `T54Facilitator(HttpClient, baseUrl)` | Delegates verify + settle to an **external t54 facilitator** (`POST /verify`, `/settle`). Verified live against the t54 testnet facilitator. |

## Usage

```csharp
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

IXrplClient xrpl = /* your connected IXrplClient */;
IX402Facilitator facilitator = new LedgerSettlingFacilitator(xrpl);
// or: IX402Facilitator facilitator = new T54Facilitator(new HttpClient());

app.MapGet("/paid", () => "premium content")
   .RequirePayment(facilitator, ctx => new PaymentRequirement
   {
       Scheme = "exact",
       Network = "xrpl:1",
       Asset = "XRP",
       PayTo = "rYourMerchantAddress...",
       Amount = "1000000",            // 1 XRP in drops
       MaxTimeoutSeconds = 600,
       Extra =
       {
           ["invoiceId"] = JsonSerializer.SerializeToElement("inv-123"),
           ["sourceTag"] = JsonSerializer.SerializeToElement(804681468) // t54 enforces SourceTag
       }
   });
```

Without a `PAYMENT-SIGNATURE` the endpoint returns **402** + the `PAYMENT-REQUIRED` challenge. With a valid signature, the facilitator verifies the destination, settles the transaction, sets `PAYMENT-RESPONSE`, and your handler runs. Implement `IX402Facilitator` for any other facilitator.

See the [`Xrpl.X402`](https://www.nuget.org/packages/Xrpl.X402) README for the full wire format, client usage, security model, and t54 interop notes.

## License

Apache-2.0. Part of [XrplCSharp](https://github.com/StaticBit-io/XrplCSharp).
