using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402.Examples.MerchantServer;
using Xrpl.X402.Examples.PayingClient;

using IntegrationUtils = XrplTests.Xrpl.ClientLib.Integration.Utils;
using XrplTests.Xrpl.ClientLib.Integration;

namespace Xrpl.X402.Tests.Integration;

/// <summary>
/// Boots the shipped example pair — <see cref="MerchantServer"/> (real Kestrel) and
/// <see cref="PayingClient"/> — against the standalone rippled and drives a full x402 flow over
/// loopback HTTP. This keeps both examples compiling and working as the SDK evolves.
/// </summary>
[TestClass]
public class X402ExampleClientServerE2E
{
    [TestMethod]
    public async Task TestIExampleMerchantServerAndPayingClientSettleXrp()
    {
        SetupIntegration runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        IXrplClient client = runner.client;
        XrplWallet payer = runner.wallet;                       // funded in SetupClient
        XrplWallet merchant = await IntegrationUtils.GenerateFundedWallet(client);

        MerchantServerOptions serverOptions = new()
        {
            RippledWsUrl = ServerUrl.serverUrl,
            ListenUrl = "http://127.0.0.1:0",                   // OS picks a free port
            MerchantAddress = merchant.ClassicAddress,
            Asset = "XRP",
            Amount = "1000000",
            MaxTimeoutSeconds = 60,
            InvoiceId = "example-clientserver-001",
            ResourceBody = "premium content",
        };

        WebApplication app = await MerchantServer.BuildAsync(serverOptions);
        await app.StartAsync();
        try
        {
            string baseUrl = MerchantServer.ResolveBoundUrl(app);

            PayingClientOptions clientOptions = new()
            {
                ResourceUrl = $"{baseUrl.TrimEnd('/')}/paid",
                PayerSeed = payer.Seed,
                RippledWsUrl = ServerUrl.serverUrl,
                Network = "xrpl:1",
                MaxAmountDrops = 10_000_000,
            };

            PaidResult result = await PayingClient.FetchAsync(clientOptions);

            Assert.AreEqual("premium content", result.Body);
            Assert.IsTrue(result.Settled, "expected the payment to settle on-ledger");
            Assert.IsFalse(string.IsNullOrEmpty(result.TxHash), "expected a settle tx hash");
            Assert.AreEqual(payer.ClassicAddress, result.Payer);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
