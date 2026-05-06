using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIMPTokenCreate : TestIMPTokenBase
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
    public async Task TestMPTokenIssuanceCreate_Basic()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate { Account = walletIssuer.ClassicAddress };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        string issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned in metadata");
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithAssetScale()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate { Account = walletIssuer.ClassicAddress, AssetScale = 2 };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithTransferFee()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            TransferFee = 1000,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithMaximumAmount()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate { Account = walletIssuer.ClassicAddress, MaximumAmount = "1000000000" };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithMetadata()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenMetadata = "48656C6C6F20576F726C64",
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanTransferFlag()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithRequireAuthFlag()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTRequireAuth,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithAllFlags()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var allFlags = MPTokenIssuanceCreateFlags.tfMPTCanLock |
                       MPTokenIssuanceCreateFlags.tfMPTCanTransfer |
                       MPTokenIssuanceCreateFlags.tfMPTCanTrade |
                       MPTokenIssuanceCreateFlags.tfMPTCanClawback;

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = allFlags,
            AssetScale = 6,
            TransferFee = 500,
            MaximumAmount = "9223372036854775807",
            MPTokenMetadata = "4D5054",
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanEscrowFlag()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanEscrow,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned for tfMPTCanEscrow");
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanTradeFlag()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTrade,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned for tfMPTCanTrade");
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanClawbackFlag()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanClawback,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned for tfMPTCanClawback");
    }
}
