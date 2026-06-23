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

using IntegrationUtils = XrplTests.Xrpl.ClientLib.Integration.Utils;
using XrplTests.Xrpl.ClientLib.Integration;

namespace Xrpl.X402.Tests.Integration;

[TestClass]
public class X402MiddlewareE2E
{
    [TestMethod]
    public async Task TestIRealMiddlewarePaysXrpAndGetsResource()
    {
        SetupIntegration runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        IXrplClient client = runner.client;
        XrplWallet payer = runner.wallet;
        XrplWallet merchant = await IntegrationUtils.GenerateFundedWallet(client);

        PaymentRequirement requirement = new()
        {
            Scheme = "exact",
            Network = "xrpl:1",
            Asset = "XRP",
            PayTo = merchant.ClassicAddress,
            Amount = "1000000",
            MaxTimeoutSeconds = 60,
            Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"A7F9C76B2EAC41A9B2D500AA76B8FA1800000000000000000000000000000004\"").RootElement }
        };

        WebApplicationBuilder appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseTestServer();
        WebApplication app = appBuilder.Build();

        IX402Facilitator facilitator = new LedgerSettlingFacilitator(client);
        app.MapGet("/resource", (HttpContext _) => Results.Text("resource"))
           .RequirePayment(facilitator, _ => requirement);

        await app.StartAsync();
        try
        {
            TestServer testServer = app.GetTestServer();

            IX402Signer signer = new XrplWalletX402Signer(client, payer);
            X402PaymentHandler x402 = new(signer, new X402ClientOptions { Network = "xrpl:1", MaxAmountDrops = 10_000_000 })
            {
                InnerHandler = testServer.CreateHandler()
            };
            HttpClient payerHttp = new(x402) { BaseAddress = new System.Uri("http://localhost/") };

            HttpResponseMessage r = await payerHttp.GetAsync("/resource");

            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.AreEqual("resource", await r.Content.ReadAsStringAsync());

            PaymentResponseEnvelope receipt = X402Base64Json.Decode<PaymentResponseEnvelope>(
                r.Headers.GetValues(X402Headers.PaymentResponse).First());
            Assert.IsTrue(receipt.Success);
            Assert.IsFalse(string.IsNullOrEmpty(receipt.Transaction));
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
