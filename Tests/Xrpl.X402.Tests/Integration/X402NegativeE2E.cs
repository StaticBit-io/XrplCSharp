using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;
using Xrpl.X402.Wire;

using IntegrationUtils = XrplTests.Xrpl.ClientLib.Integration.Utils;
using XrplTests.Xrpl.ClientLib.Integration;

namespace Xrpl.X402.Tests.Integration;

[TestClass]
public class X402NegativeE2E
{
    [TestMethod]
    public async Task TestIRefusesOverCapNoSigning()
    {
        SetupIntegration runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        IXrplClient client = runner.client;
        XrplWallet merchant = await IntegrationUtils.GenerateFundedWallet(client);

        // Requirement demanding 5000 XRP (5_000_000_000 drops) — far above payer's cap
        PaymentRequirement requirement = new()
        {
            Scheme = "exact",
            Network = "xrpl:1",
            Asset = "XRP",
            PayTo = merchant.ClassicAddress,
            Amount = "5000000000",
            MaxTimeoutSeconds = 60,
            Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"inv-e2e-overcap\"").RootElement }
        };

        WebApplication app = new TestMerchant(new TestFacilitator(client), requirement).Build();
        await app.StartAsync();
        try
        {
            TestServer testServer = app.GetTestServer();

            // Payer cap: 10 XRP (10_000_000 drops) << 5000 XRP requirement
            IX402Signer signer = new XrplWalletX402Signer(client, runner.wallet);
            X402PaymentHandler x402 = new(signer, new X402ClientOptions
            {
                Network = "xrpl:1",
                MaxAmountDrops = 10_000_000
            })
            {
                InnerHandler = testServer.CreateHandler()
            };
            System.Net.Http.HttpClient http = new(x402) { BaseAddress = new System.Uri("http://localhost/") };

            X402PaymentException ex = await Assert.ThrowsExactlyAsync<X402PaymentException>(
                () => http.GetAsync("/resource"));

            Assert.AreEqual("amount_over_cap", ex.Reason);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
