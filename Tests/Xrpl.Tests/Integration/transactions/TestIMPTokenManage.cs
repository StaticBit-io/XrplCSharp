using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIMPTokenManage : TestIMPTokenBase
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

    #region MPTokenIssuanceDestroy

    [TestMethod]
    public async Task TestMPTokenIssuanceDestroy_Basic()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var createTx = new MPTokenIssuanceCreate { Account = walletIssuer.ClassicAddress };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId);

        var destroyTx = new MPTokenIssuanceDestroy
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        destroyTx = await client.Autofill(destroyTx);

        var result = await client.SubmitAndWait(destroyTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceDestroy_FailsWithOutstandingHolders()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        XrplWallet walletHolder1 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        var authResult = await client.SubmitAndWait(authTx, walletHolder1, true);
        ValidateResult(authResult);

        var paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder1.ClassicAddress,
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = "1000" },
        };
        paymentTx = await client.Autofill(paymentTx);
        var paymentResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(paymentResult);

        var destroyTx = new MPTokenIssuanceDestroy
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        destroyTx = await client.Autofill(destroyTx);

        await Helper.ThrowsExceptionAsync<RippleException>(
            () => client.SubmitAndWait(destroyTx, walletIssuer, true),
            "Final tx result is not success: tecHAS_OBLIGATIONS");
    }

    #endregion

    #region MPTokenIssuanceSet

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_GlobalLock()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);

        var result = await client.SubmitAndWait(lockTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_GlobalUnlock()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);
        await client.SubmitAndWait(lockTx, walletIssuer, true);

        var unlockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTUnlock,
        };
        unlockTx = await client.Autofill(unlockTx);

        var result = await client.SubmitAndWait(unlockTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_LockSpecificHolder()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        XrplWallet walletHolder1 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock | MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        await client.SubmitAndWait(authTx, walletHolder1, true);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder1.ClassicAddress,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);

        var result = await client.SubmitAndWait(lockTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_UnlockSpecificHolder()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        XrplWallet walletHolder1 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock | MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        await client.SubmitAndWait(authTx, walletHolder1, true);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder1.ClassicAddress,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);
        var lockResult = await client.SubmitAndWait(lockTx, walletIssuer, true);
        ValidateResult(lockResult);

        var lockedFlags = GetMPTokenFlagsFromMeta(lockResult);
        Assert.IsTrue(lockedFlags.HasValue, "MPToken flags should exist in lock tx metadata");
        Assert.IsTrue((lockedFlags.Value & MPTokenFlags.lsfMPTLocked) != 0, "MPToken should have lock flag set after lock");

        var unlockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder1.ClassicAddress,
            Flags = MPTokenIssuanceSetFlags.tfMPTUnlock,
        };
        unlockTx = await client.Autofill(unlockTx);

        var unlockResult = await client.SubmitAndWait(unlockTx, walletIssuer, true);
        ValidateResult(unlockResult);

        var unlockedFlags = GetMPTokenFlagsFromMeta(unlockResult);
        Assert.IsTrue(unlockedFlags.HasValue, "MPToken flags should exist in unlock tx metadata");
        Assert.IsTrue((unlockedFlags.Value & MPTokenFlags.lsfMPTLocked) == 0, "MPToken should have lock flag cleared after unlock");
    }

    #endregion
}
