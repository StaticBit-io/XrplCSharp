using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("LedgerStateFix")]
public class TestILedgerStateFix
{
    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    /// <summary>
    /// LedgerStateFix only repairs real corruption (a broken NFT page chain), so on a
    /// healthy account rippled answers tecFAILED_PROCESSING. The transaction is submitted
    /// WITHOUT fail_hard on purpose: with it a tec result is dropped from the open ledger
    /// and never validated, so nothing would prove the node accepted the transaction at all.
    /// Without it the tec result is applied, the fee is claimed and the transaction reaches
    /// a validated ledger like any other.
    /// </summary>
    [TestMethod]
    public async Task TestLedgerStateFix_Basic_ReachesValidatedLedger()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        LedgerStateFix tx = new LedgerStateFix
        {
            Account = wallet.ClassicAddress,
            LedgerFixType = 1,
            Owner = wallet.ClassicAddress,
        };
        // Autofill sets the reserve-level fee LedgerStateFix requires (>= owner reserve)
        tx = await client.Autofill(tx);

        string result;
        try
        {
            TransactionSummary summary = await client.SubmitAndWait(tx, wallet, autofill: false);
            result = summary.Meta?.TransactionResult;
        }
        catch (TransactionFailedException ex)
        {
            result = ex.Message;
        }

        Assert.IsTrue(
            result is not null && (result.Contains("tesSUCCESS") || result.Contains("tecFAILED_PROCESSING")),
            $"Expected tesSUCCESS or tecFAILED_PROCESSING, got: {result}");
    }
}
