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

## Configuration

Both apps read their settings from their own **`appsettings.json`** (the `X402` section) — strict
JSON, so it lists every knob. Each value can be overridden by an environment variable (e.g.
`MERCHANT_ADDRESS`, `PAYER_SEED`) without editing the file.

### Server ([`X402.MerchantServer/appsettings.json`](X402.MerchantServer/appsettings.json))

| Key | Env override | Meaning |
|---|---|---|
| `RippledWsUrl` | `RIPPLED_WS` | WebSocket URL of the rippled node that settles payments. |
| `ListenUrl` | `LISTEN_URL` | URL Kestrel binds to. Empty → use the launch profile / `ASPNETCORE_URLS` (`http://127.0.0.1:5402`). Use a `:0` port for an OS-assigned one. |
| `MerchantAddress` | `MERCHANT_ADDRESS` | **Required.** Classic XRPL address that receives the payment. |
| `Network` | `NETWORK` | CAIP-2 network advertised in the 402 challenge (e.g. `xrpl:1`). |
| `Asset` | `ASSET` | `XRP` (drops) or a 40-hex currency code such as RLUSD (then also set `IouIssuer`). |
| `Amount` | `AMOUNT` | Amount to charge: drops for XRP, a decimal value string for IOUs. |
| `IouIssuer` | `IOU_ISSUER` | Issuer address for IOU assets. Required when `Asset` is not XRP. |
| `MaxTimeoutSeconds` | — | Seconds the signed payment stays valid (maps to `LastLedgerSequence`). |
| `InvoiceId` | `INVOICE_ID` | Invoice id echoed back in `extra.invoiceId`. |
| `ResourceBody` | — | Body returned once the payment settles. |

The port comes from the launch profile
([`launchSettings.json`](X402.MerchantServer/Properties/launchSettings.json), `http://127.0.0.1:5402`)
unless `ListenUrl` is set. Runs from Visual Studio (F5) or `dotnet run`. The committed
`MerchantAddress` is a throwaway standalone-funded sample — replace it with your own.

### Client ([`X402.PayingClient/appsettings.json`](X402.PayingClient/appsettings.json))

| Key | Env override | Meaning |
|---|---|---|
| `RippledWsUrl` | `RIPPLED_WS` | WebSocket URL of the rippled node used to autofill/sign the payment. |
| `ResourceUrl` | `RESOURCE_URL` | Absolute URL of the payment-protected resource (the merchant's `/paid`). |
| `PayerSeed` | `PAYER_SEED` | **Required.** Seed of the funded payer wallet. **Do not commit a real seed** — set it via `PAYER_SEED` or only in a local, untracked copy. |
| `Network` | `NETWORK` | CAIP-2 network the client is willing to pay on. |
| `MaxAmountDrops` | — | Hard cap for XRP payments, in drops. |
| `IouValueCaps` | — | Per-issuer IOU/RLUSD caps: `{ "<issuerAddress>": <decimal cap> }`. Required to pay any IOU. |

You need a reachable `rippled` (a standalone node or testnet) and two funded wallets — one for the
merchant (receives) and one for the payer (signs/funds). For a standalone node, see the integration
setup in the repo root [`README`](../README.md).

## Run them by hand

**1. Start the merchant** (Visual Studio F5, or terminal A). Set `MerchantAddress` in
`appsettings.json`, then:

```bash
dotnet run --project Examples/X402.MerchantServer
```

For an IOU/RLUSD price, set `Asset` (40-hex currency code), `IouIssuer`, and an `Amount` decimal
value (e.g. `2.5`) in `appsettings.json`; the payer needs a trustline to that issuer.

**2. Pay from the client** (terminal B). The seed is a secret, so pass it via the environment:

```bash
PAYER_SEED="sEdPayerSeed..." dotnet run --project Examples/X402.PayingClient
```

The client prints the resource body plus the settlement receipt (success, tx hash, payer). The
server settles **but never submits the client's funds itself** — the client signs locally and the
merchant's facilitator settles, per the t54 scheme. See the
[full x402 guide](https://staticbit-io.github.io/XrplCSharp/X402-Guide.html) for the complete
price → pay → receipt flow.
