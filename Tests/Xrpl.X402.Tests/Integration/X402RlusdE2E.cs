using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;
using Xrpl.X402;
using Xrpl.X402.Wire;

using IntegrationUtils = XrplTests.Xrpl.ClientLib.Integration.Utils;
using XrplTests.Xrpl.ClientLib.Integration;

namespace Xrpl.X402.Tests.Integration;

[TestClass]
public class X402RlusdE2E
{
    // Canonical 40-hex currency code for RLUSD
    private const string Rlusd = "524C555344000000000000000000000000000000";

    [TestMethod]
    public async Task TestIPaysRlusdAndGetsResource()
    {
        // 1. Setup client + funded payer
        SetupIntegration runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        IXrplClient client = runner.client;
        XrplWallet payer = runner.wallet;

        // 2. Generate + fund merchant and issuer wallets
        XrplWallet merchant = await IntegrationUtils.GenerateFundedWallet(client);
        XrplWallet issuer  = await IntegrationUtils.GenerateFundedWallet(client);

        // 3. Enable DefaultRipple on issuer so payments can ripple through issuer→merchant
        AccountSet enableRipple = new AccountSet
        {
            Account = issuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple
        };
        await IntegrationUtils.TestTransaction(client, enableRipple.ToDictionary(), issuer);

        // 4. Set trustline: payer → issuer (limit 1 000 000 RLUSD)
        TrustSet payerTrust = new TrustSet
        {
            Account = payer.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = Rlusd,
                Issuer = issuer.ClassicAddress,
                Value = "1000000"
            }
        };
        await IntegrationUtils.TestTransaction(client, payerTrust.ToDictionary(), payer);

        // 4. Set trustline: merchant → issuer (limit 1 000 000 RLUSD)
        TrustSet merchantTrust = new TrustSet
        {
            Account = merchant.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = Rlusd,
                Issuer = issuer.ClassicAddress,
                Value = "1000000"
            }
        };
        await IntegrationUtils.TestTransaction(client, merchantTrust.ToDictionary(), merchant);

        // 5. Issuer issues 100 RLUSD to payer
        Payment issue = new Payment
        {
            Account = issuer.ClassicAddress,
            Destination = payer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = Rlusd,
                Issuer = issuer.ClassicAddress,
                Value = "100"
            }
        };
        await IntegrationUtils.TestTransaction(client, issue.ToDictionary(), issuer);

        // 6. Build PaymentRequirement for 2.5 RLUSD
        PaymentRequirement requirement = new()
        {
            Scheme = "exact",
            Network = "xrpl:1",
            Asset = Rlusd,
            PayTo = merchant.ClassicAddress,
            Amount = "2.5",
            MaxTimeoutSeconds = 60,
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse("\"A7F9C76B2EAC41A9B2D500AA76B8FA1800000000000000000000000000000002\"").RootElement,
                ["issuer"]    = JsonDocument.Parse($"\"{issuer.ClassicAddress}\"").RootElement,
            }
        };

        // 7. Stand up TestMerchant
        WebApplication app = new TestMerchant(new TestFacilitator(client), requirement).Build();
        await app.StartAsync();
        try
        {
            TestServer testServer = app.GetTestServer();

            // 8. Build payer side
            IX402Signer signer = new XrplWalletX402Signer(client, payer);
            X402PaymentHandler x402 = new(signer, new X402ClientOptions
            {
                Network = "xrpl:1",
                IouValueCaps = { [issuer.ClassicAddress] = 10m }
            })
            {
                InnerHandler = testServer.CreateHandler()
            };
            HttpClient payerHttp = new(x402) { BaseAddress = new System.Uri("http://localhost/") };

            // 9. GET /resource — expect 200 OK + successful receipt
            HttpResponseMessage r = await payerHttp.GetAsync("/resource");

            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.AreEqual("resource", await r.Content.ReadAsStringAsync());

            PaymentResponseEnvelope receipt = X402Base64Json.Decode<PaymentResponseEnvelope>(
                r.Headers.GetValues(X402Headers.PaymentResponse).First());
            Assert.IsTrue(receipt.Success, $"receipt.Success=false, reason={receipt.ErrorReason}");
            Assert.AreEqual(payer.ClassicAddress, receipt.Payer);
            Assert.IsFalse(string.IsNullOrEmpty(receipt.Transaction));
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
