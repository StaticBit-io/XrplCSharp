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
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor);

        XChainBridgeModel bridge = CreateXrpTestBridge(walletDoor.ClassicAddress);

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
        XrplWallet walletUser = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletUser);

        XChainBridgeModel bridge = CreateXrpTestBridge(walletDoor.ClassicAddress);

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
            OtherChainSource = walletUser.ClassicAddress,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletUser, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainModifyBridge_UpdateSignatureReward()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor);

        XChainBridgeModel bridge = CreateXrpTestBridge(walletDoor.ClassicAddress);

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
        XrplWallet walletUser = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletUser);

        XChainBridgeModel bridge = CreateXrpTestBridge(walletDoor.ClassicAddress);

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
            OtherChainDestination = XrplWallet.Generate().ClassicAddress,
        };
        commitTx = await client.Autofill(commitTx);

        TransactionSummary result = await client.SubmitAndWait(commitTx, walletUser, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainAccountCreateCommit_Basic()
    {
        XrplWallet walletDoor = XrplWallet.Generate();
        XrplWallet walletUser = XrplWallet.Generate();
        XrplWallet walletNewAccount = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletDoor, walletUser);

        XChainBridgeModel bridge = CreateXrpTestBridge(walletDoor.ClassicAddress);

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

    // ──────────────────────────────────────────────────────────────
    // IOU-IOU bridge tests
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TestXChainCreateBridge_Iou()
    {
        // lockingDoor = door account on locking chain (can differ from issuer)
        // lockingIssuer = token issuer on locking chain
        // issuingDoor = door account on issuing chain (MUST be the token issuer)
        XrplWallet walletLockingDoor = XrplWallet.Generate();
        XrplWallet walletLockingIssuer = XrplWallet.Generate();
        XrplWallet walletIssuingDoor = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
            walletLockingDoor, walletLockingIssuer, walletIssuingDoor);

        // Locking door needs TrustLine to its issuer
        await SetupTrustLine(client, walletLockingDoor, walletLockingIssuer.ClassicAddress);
        // Issuing door IS the issuer — no TrustLine needed to itself

        XChainBridgeModel bridge = CreateIouTestBridge(
            walletLockingDoor.ClassicAddress, walletLockingIssuer.ClassicAddress,
            walletIssuingDoor.ClassicAddress);

        XChainCreateBridge tx = new XChainCreateBridge
        {
            Account = walletLockingDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletLockingDoor, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainCreateClaimID_Iou()
    {
        XrplWallet walletLockingDoor = XrplWallet.Generate();
        XrplWallet walletLockingIssuer = XrplWallet.Generate();
        XrplWallet walletIssuingDoor = XrplWallet.Generate();
        XrplWallet walletUser = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
            walletLockingDoor, walletLockingIssuer, walletIssuingDoor, walletUser);

        await SetupTrustLine(client, walletLockingDoor, walletLockingIssuer.ClassicAddress);

        XChainBridgeModel bridge = CreateIouTestBridge(
            walletLockingDoor.ClassicAddress, walletLockingIssuer.ClassicAddress,
            walletIssuingDoor.ClassicAddress);

        // Create the bridge first
        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletLockingDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeResult = await client.SubmitAndWait(createBridge, walletLockingDoor, true);
        ValidateResult(bridgeResult);

        // Create claim ID
        XChainCreateClaimID tx = new XChainCreateClaimID
        {
            Account = walletUser.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
            OtherChainSource = walletUser.ClassicAddress,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletUser, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainCommit_Iou()
    {
        XrplWallet walletLockingDoor = XrplWallet.Generate();
        XrplWallet walletLockingIssuer = XrplWallet.Generate();
        XrplWallet walletIssuingDoor = XrplWallet.Generate();
        XrplWallet walletUser = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
            walletLockingDoor, walletLockingIssuer, walletIssuingDoor, walletUser);

        // Enable DefaultRipple on issuer BEFORE creating TrustLines —
        // required for IOU transfers between user and door through the issuer
        await EnableDefaultRipple(client, walletLockingIssuer);

        await SetupTrustLine(client, walletLockingDoor, walletLockingIssuer.ClassicAddress);
        // User needs a TrustLine to the locking issuer to hold the IOU
        await SetupTrustLine(client, walletUser, walletLockingIssuer.ClassicAddress);

        // Issue tokens from locking issuer to user
        Payment issueTokens = new Payment
        {
            Account = walletLockingIssuer.ClassicAddress,
            Destination = walletUser.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = TestCurrencyCode,
                Issuer = walletLockingIssuer.ClassicAddress,
                Value = "1000",
            },
        };
        issueTokens = await client.Autofill(issueTokens);
        TransactionSummary issueResult = await client.SubmitAndWait(issueTokens, walletLockingIssuer, true);
        ValidateResult(issueResult);

        XChainBridgeModel bridge = CreateIouTestBridge(
            walletLockingDoor.ClassicAddress, walletLockingIssuer.ClassicAddress,
            walletIssuingDoor.ClassicAddress);

        // Create bridge
        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletLockingDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeRes = await client.SubmitAndWait(createBridge, walletLockingDoor, true);
        ValidateResult(bridgeRes);

        // Create claim ID (needed before commit)
        XChainCreateClaimID claimIdTx = new XChainCreateClaimID
        {
            Account = walletUser.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
            OtherChainSource = walletUser.ClassicAddress,
        };
        claimIdTx = await client.Autofill(claimIdTx);
        TransactionSummary claimIdResult = await client.SubmitAndWait(claimIdTx, walletUser, true);
        ValidateResult(claimIdResult);

        // Commit IOU tokens to the bridge
        XChainCommit commitTx = new XChainCommit
        {
            Account = walletUser.ClassicAddress,
            XChainBridge = bridge,
            XChainClaimID = "1",
            Amount = new Currency
            {
                CurrencyCode = TestCurrencyCode,
                Issuer = walletLockingIssuer.ClassicAddress,
                Value = "100",
            },
            OtherChainDestination = XrplWallet.Generate().ClassicAddress,
        };
        commitTx = await client.Autofill(commitTx);

        TransactionSummary result = await client.SubmitAndWait(commitTx, walletUser, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestXChainModifyBridge_Iou()
    {
        XrplWallet walletLockingDoor = XrplWallet.Generate();
        XrplWallet walletLockingIssuer = XrplWallet.Generate();
        XrplWallet walletIssuingDoor = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
            walletLockingDoor, walletLockingIssuer, walletIssuingDoor);

        await SetupTrustLine(client, walletLockingDoor, walletLockingIssuer.ClassicAddress);

        XChainBridgeModel bridge = CreateIouTestBridge(
            walletLockingDoor.ClassicAddress, walletLockingIssuer.ClassicAddress,
            walletIssuingDoor.ClassicAddress);

        // Create bridge
        XChainCreateBridge createBridge = new XChainCreateBridge
        {
            Account = walletLockingDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
        };
        createBridge = await client.Autofill(createBridge);
        TransactionSummary bridgeRes = await client.SubmitAndWait(createBridge, walletLockingDoor, true);
        ValidateResult(bridgeRes);

        // Modify the bridge
        XChainModifyBridge modifyTx = new XChainModifyBridge
        {
            Account = walletLockingDoor.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = new Currency { Value = "200", CurrencyCode = "XRP" },
        };
        modifyTx = await client.Autofill(modifyTx);

        TransactionSummary result = await client.SubmitAndWait(modifyTx, walletLockingDoor, true);
        ValidateResult(result);
    }
}
