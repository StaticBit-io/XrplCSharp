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
[Ignore("LedgerStateFix is an admin-only transaction; no standalone amendment available")]
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

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
    }

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
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }
}
