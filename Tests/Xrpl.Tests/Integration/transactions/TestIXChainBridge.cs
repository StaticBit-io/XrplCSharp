using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("XChain")]
public class TestIXChainBridge : TestIXChainBridgeBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestXChainCreateBridge_Basic()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        XrplWallet walletIssuing = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletIssuing);

        XChainBridgeModel bridge = CreateTestBridge(walletDoor.ClassicAddress, walletIssuing.ClassicAddress);

        XChainCreateBridge tx = new XChainCreateBridge
        {
            Account = walletDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
            MinAccountCreateAmount = new Currency { Value = "10000000", CurrencyCode = "XRP" },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletDoor, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainCreateClaimID_Basic()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        XrplWallet walletIssuing = XrplWallet.Generate();
        XrplWallet walletUser = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletIssuing, walletUser);

        XChainBridgeModel bridge = CreateTestBridge(walletDoor.ClassicAddress, walletIssuing.ClassicAddress);

        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeResult = await client.SubmitAndWait(createBridge, walletDoor, true);
        ValidateResult(bridgeResult);

        XChainCreateClaimID tx = new XChainCreateClaimID
        {
            Account = walletUser.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
            OtherChainSource = walletIssuing.ClassicAddress,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletUser, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainModifyBridge_UpdateSignatureReward()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        XrplWallet walletIssuing = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletIssuing);

        XChainBridgeModel bridge = CreateTestBridge(walletDoor.ClassicAddress, walletIssuing.ClassicAddress);

        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeResult = await client.SubmitAndWait(createBridge, walletDoor, true);
        ValidateResult(bridgeResult);

        XChainModifyBridge modifyTx = new XChainModifyBridge
        {
            Account = walletDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "200", CurrencyCode = "XRP" },
        };
        modifyTx = await client.Autofill(modifyTx);

        TransactionSummary result = await client.SubmitAndWait(modifyTx, walletDoor, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainCommit_Basic()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        XrplWallet walletIssuing = XrplWallet.Generate();
        XrplWallet walletUser = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletIssuing, walletUser);

        XChainBridgeModel bridge = CreateTestBridge(walletDoor.ClassicAddress, walletIssuing.ClassicAddress);

        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeResult = await client.SubmitAndWait(createBridge, walletDoor, true);
        ValidateResult(bridgeResult);

        XChainCommit commitTx = new XChainCommit
        {
            Account = walletUser.ClassicAddress,
            XChainBridge = bridge,
            XChainClaimID = "1",
            Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
            OtherChainDestination = walletIssuing.ClassicAddress,
        };
        commitTx = await client.Autofill(commitTx);

        TransactionSummary result = await client.SubmitAndWait(commitTx, walletUser, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainAccountCreateCommit_Basic()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        XrplWallet walletIssuing = XrplWallet.Generate();
        XrplWallet walletUser = XrplWallet.Generate();
        XrplWallet walletNewAccount = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletIssuing, walletUser);

        XChainBridgeModel bridge = CreateTestBridge(walletDoor.ClassicAddress, walletIssuing.ClassicAddress);

        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
            MinAccountCreateAmount = new Currency { Value = "10000000", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeResult = await client.SubmitAndWait(createBridge, walletDoor, true);
        ValidateResult(bridgeResult);

        XChainAccountCreateCommit tx = new XChainAccountCreateCommit
        {
            Account = walletUser.ClassicAddress,
            XChainBridge = bridge,
            Destination = walletNewAccount.ClassicAddress,
            Amount = new Currency { Value = "20000000", CurrencyCode = "XRP" },
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletUser, true);
        ValidateResult(result);
    }
}
