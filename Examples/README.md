# Xrpl.X402 examples

A runnable **client + server** pair demonstrating the x402 (HTTP-402) agentic-payment flow on the
XRP Ledger.

| Project | Role |
|---|---|
| [`X402.MerchantServer`](X402.MerchantServer) | ASP.NET Core minimal API. `GET /paid` is protected by `RequirePayment`; an unpaid request gets **402 + PAYMENT-REQUIRED**, a signed one is settled on-ledger by `LedgerSettlingFacilitator` and the resource is returned with **PAYMENT-RESPONSE**. |
| [`X402.PayingClient`](X402.PayingClient) | Console app. Wraps an `HttpClient` with `X402PaymentHandler`, so the 402 is paid (signed locally, settled by the merchant) and retried transparently. |

Both are wired into CI: the integration test
[`X402ExampleClientServerE2E`](../Tests/Xrpl.X402.Tests/Integration/X402ExampleClientServerE2E.cs)
boots the server on real Kestrel and drives the client against it over loopback HTTP, against the
standalone `rippled` used by the rest of the integration suite.

## Run them by hand

You need a reachable `rippled` (a standalone node or testnet) and two funded wallets — one for the
merchant (receives) and one for the payer (signs/funds). For a standalone node, see the integration
setup in the repo root [`README`](../README.md).

**1. Start the merchant** (terminal A):

```bash
export RIPPLED_WS="ws://localhost:6006"
export MERCHANT_ADDRESS="rMerchantClassicAddress..."
export LISTEN_URL="http://127.0.0.1:5402"
export AMOUNT="1000000"          # 1 XRP, in drops
dotnet run --project Examples/X402.MerchantServer
```

For an IOU/RLUSD price, also set `ASSET` (40-hex currency code), `IOU_ISSUER`, and an `AMOUNT`
decimal value (e.g. `2.5`); the payer needs a trustline to that issuer.

**2. Pay from the client** (terminal B):

```bash
export RIPPLED_WS="ws://localhost:6006"
export RESOURCE_URL="http://127.0.0.1:5402/paid"
export PAYER_SEED="sEdPayerSeed..."
dotnet run --project Examples/X402.PayingClient
```

The client prints the resource body plus the settlement receipt (success, tx hash, payer). The
server settles **but never submits the client's funds itself** — the client signs locally and the
merchant's facilitator settles, per the t54 scheme. See the
[full x402 guide](https://staticbit-io.github.io/XrplCSharp/X402-Guide.html) for the complete
price → pay → receipt flow.
