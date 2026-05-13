using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("LedgerStateFix")]
//[Ignore("LedgerStateFix requires the LedgerStateFix amendment which may not be available on standalone")]
public class TestILedgerStateFix
{
    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestLedgerStateFix_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        LedgerStateFix tx = new LedgerStateFix
        {
            Account = wallet.ClassicAddress,
            LedgerFixType = 1,
            Owner = wallet.ClassicAddress,
        };
        // Autofill automatically sets the reserve fee for LedgerStateFix (>= owner reserve, 0.2 XRP)
        tx = await client.Autofill(tx);

        // Use fail_hard to avoid paying the high fee if the transaction would fail
        var result = await client.Submit(tx, wallet, true, true);

        // tecFAILED_PROCESSING is expected on a healthy account — LedgerStateFix only
        // succeeds when there is an actual ledger corruption to repair (e.g. broken NFT directory).
        // On a fresh wallet with no issues, the network correctly rejects the fix attempt.
        string txResult = result.EngineResult;
        Assert.IsTrue(
            txResult is "tesSUCCESS" or "tecFAILED_PROCESSING",
            $"Expected tesSUCCESS or tecFAILED_PROCESSING, got: {txResult}");
    }
}
