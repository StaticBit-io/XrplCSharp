using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Integration;

/// <summary>
/// Live interop test that pins our x402 wire format against the real t54 facilitator
/// (<c>https://xrpl-facilitator-testnet.t54.ai</c>) on public testnet.
/// <para>
/// This test is intentionally <see cref="IgnoreAttribute">ignored</see> in CI.
/// Run manually to verify end-to-end interoperability with the live t54 service.
/// </para>
/// <para>
/// Faucet used: <c>WalletSugar.FundWallet(IXrplClient)</c> extension, which POSTs to
/// <c>https://faucet.altnet.rippletest.net/accounts</c> (auto-detected from "altnet"
/// in the WebSocket URL).
/// </para>
/// <para>
/// Known interop finding (2026-06-22): t54 testnet returns
/// <c>verify_failed:invalid_payload</c> for correctly signed XRPL transactions
/// produced by this SDK.  The transactions are accepted by testnet itself
/// (<c>tesSUCCESS</c>), so the payload structure and signing are correct.
/// The rejection originates in t54's Python-side signature-verification step and
/// appears to be a t54 backend issue rather than a wire-format divergence.
/// See NEEDS_CONTEXT note in the test report.
/// </para>
/// </summary>
[TestClass]
public class X402T54LiveInteropTests
{
    private const string TestnetUrl = "wss://s.altnet.rippletest.net:51233";

    /// <summary>
    /// Full end-to-end x402 flow against the real t54 facilitator on testnet.
    /// Faucet: <c>client.FundWallet(wallet)</c> →
    /// POST <c>https://faucet.altnet.rippletest.net/accounts</c>.
    /// </summary>
    [Ignore("Live external deps: public testnet faucet + t54 facilitator; run manually")]
    [TestMethod]
    public async Task TestT54LiveSettlesXrpOnTestnet()
    {
        // ── 1. Connect to public testnet ───────────────────────────────────────────
        XrplClient client = new(TestnetUrl);
        await client.Connect();

        try
        {
            // ── 2. Fund payer and merchant via testnet faucet ──────────────────────
            // WalletSugar.FundWallet POSTs to https://faucet.altnet.rippletest.net/accounts
            // and polls the ledger until the balance increases.
            XrplWallet payer = XrplWallet.Generate();
            XrplWallet merchant = XrplWallet.Generate();

            WalletSugar.Funded payerFunded = await client.FundWallet(payer);
            WalletSugar.Funded merchantFunded = await client.FundWallet(merchant);

            Console.WriteLine($"[T54-LIVE] payer    = {payer.ClassicAddress} ({payerFunded.Balance} XRP)");
            Console.WriteLine($"[T54-LIVE] merchant = {merchant.ClassicAddress} ({merchantFunded.Balance} XRP)");

            // ── 3. Build a PaymentRequirement for 1 XRP (1 000 000 drops) ─────────
            string invoiceId = $"inv-t54-live-{Guid.NewGuid():N}";
            PaymentRequirement requirement = new()
            {
                Scheme = "exact",
                Network = "xrpl:1",
                Asset = "XRP",
                PayTo = merchant.ClassicAddress,
                Amount = "1000000",
                MaxTimeoutSeconds = 600,
                Extra = new()
                {
                    ["invoiceId"] = JsonDocument.Parse($"\"{invoiceId}\"").RootElement
                }
            };

            Console.WriteLine($"[T54-LIVE] requirement.PayTo = {requirement.PayTo}");
            Console.WriteLine($"[T54-LIVE] invoiceId         = {invoiceId}");

            // ── 4. Stand up TestServer merchant backed by T54Facilitator ──────────
            WebApplicationBuilder appBuilder = WebApplication.CreateBuilder();
            appBuilder.WebHost.UseTestServer();
            WebApplication app = appBuilder.Build();

            T54Facilitator t54 = new(new HttpClient());
            app.MapGet("/resource", (HttpContext _) => Results.Text("resource"))
               .RequirePayment(t54, _ => requirement);

            await app.StartAsync();
            try
            {
                TestServer testServer = app.GetTestServer();

                // ── 5. Build payer side ────────────────────────────────────────────
                IX402Signer signer = new XrplWalletX402Signer(client, payer);
                X402PaymentHandler x402 = new(
                    signer,
                    new X402ClientOptions { Network = "xrpl:1", MaxAmountDrops = 10_000_000 })
                {
                    InnerHandler = testServer.CreateHandler()
                };
                HttpClient payerHttp = new(x402)
                {
                    BaseAddress = new Uri("http://localhost/")
                };

                // ── 6. GET /resource — triggers 402 → sign → retry via t54 ────────
                HttpResponseMessage r = await payerHttp.GetAsync("/resource");

                Console.WriteLine($"[T54-LIVE] HTTP status = {r.StatusCode}");

                // ── 7. Decode and inspect the PAYMENT-RESPONSE header ─────────────
                Assert.AreEqual(HttpStatusCode.OK, r.StatusCode,
                    "Expected 200 OK after t54 settlement");

                string body = await r.Content.ReadAsStringAsync();
                Assert.AreEqual("resource", body);

                PaymentResponseEnvelope receipt = X402Base64Json.Decode<PaymentResponseEnvelope>(
                    r.Headers.GetValues(X402Headers.PaymentResponse).First());

                Console.WriteLine($"[T54-LIVE] receipt.Success      = {receipt.Success}");
                Console.WriteLine($"[T54-LIVE] receipt.Transaction   = {receipt.Transaction}");
                Console.WriteLine($"[T54-LIVE] receipt.Network       = {receipt.Network}");
                Console.WriteLine($"[T54-LIVE] receipt.Payer         = {receipt.Payer}");
                Console.WriteLine($"[T54-LIVE] receipt.ErrorReason   = {receipt.ErrorReason}");

                Assert.IsTrue(receipt.Success,
                    $"t54 settlement failed: errorReason={receipt.ErrorReason}");
                Assert.IsFalse(string.IsNullOrEmpty(receipt.Transaction),
                    "Expected a non-empty transaction hash from t54");
            }
            finally
            {
                await app.StopAsync();
            }
        }
        finally
        {
            await client.Disconnect();
        }
    }
}
