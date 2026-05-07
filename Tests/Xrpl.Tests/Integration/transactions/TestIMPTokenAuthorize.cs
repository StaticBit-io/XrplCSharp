using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIMPTokenAuthorize : TestIMPTokenBase
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

    #region MPTokenAuthorize

    [TestMethod]
    public async Task TestMPTokenAuthorize_HolderOptIn()
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

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);

        var result = await client.SubmitAndWait(authTx, walletHolder1, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenAuthorize_IssuerAuthorizesHolder()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        XrplWallet walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTRequireAuth,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var holderAuthTx = new MPTokenAuthorize
        {
            Account = walletHolder2.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        holderAuthTx = await client.Autofill(holderAuthTx);
        await client.SubmitAndWait(holderAuthTx, walletHolder2, true);

        var issuerAuthTx = new MPTokenAuthorize
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder2.ClassicAddress,
        };
        issuerAuthTx = await client.Autofill(issuerAuthTx);

        var result = await client.SubmitAndWait(issuerAuthTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenAuthorize_HolderUnauthorize()
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

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        await client.SubmitAndWait(authTx, walletHolder1, true);

        var unauthTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenAuthorizeFlags.tfMPTUnauthorize,
        };
        unauthTx = await client.Autofill(unauthTx);

        var result = await client.SubmitAndWait(unauthTx, walletHolder1, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenAuthorize_IssuerRevokesAuthorization()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        XrplWallet walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTRequireAuth,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var holderAuthTx = new MPTokenAuthorize
        {
            Account = walletHolder2.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        holderAuthTx = await client.Autofill(holderAuthTx);
        await client.SubmitAndWait(holderAuthTx, walletHolder2, true);

        var issuerAuthTx = new MPTokenAuthorize
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder2.ClassicAddress,
        };
        issuerAuthTx = await client.Autofill(issuerAuthTx);
        await client.SubmitAndWait(issuerAuthTx, walletIssuer, true);

        var revokeTx = new MPTokenAuthorize
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder2.ClassicAddress,
            Flags = MPTokenAuthorizeFlags.tfMPTUnauthorize,
        };
        revokeTx = await client.Autofill(revokeTx);

        var result = await client.SubmitAndWait(revokeTx, walletIssuer, true);
        ValidateResult(result);
    }

    #endregion

    #region MPT Payment

    [TestMethod]
    public async Task TestMPTPayment_TransferAndVerifyBalance()
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
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned in metadata");
        Console.WriteLine($"Created MPT Issuance: {issuanceId}");

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        var authResult = await client.SubmitAndWait(authTx, walletHolder1, true);
        ValidateResult(authResult);
        Console.WriteLine("Holder authorized for MPT");

        const ulong transferAmount = 1000;
        var paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder1.ClassicAddress,
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = transferAmount.ToString() },
        };
        paymentTx = await client.Autofill(paymentTx);
        var paymentResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(paymentResult);
        Console.WriteLine($"Payment sent: {transferAmount} MPT");

        var ledgerEntryRequest = new LedgerEntryRequest
        {
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
            MPToken = new MPTokenQuery
            {
                Account = walletHolder1.ClassicAddress,
                MPTokenIssuanceID = issuanceId,
            },
        };
        var ledgerEntryResponse = await client.LedgerEntry(ledgerEntryRequest);

        Assert.IsNotNull(ledgerEntryResponse, "LedgerEntry response should not be null");
        Assert.IsNotNull(ledgerEntryResponse.Node, "LedgerEntry node should not be null");

        var mpToken = ledgerEntryResponse.Node as LOMPToken;
        Assert.IsNotNull(mpToken, "Node should be LOMPToken type");
        Assert.AreEqual(walletHolder1.ClassicAddress, mpToken.Account, "MPToken account should match holder");
        Assert.AreEqual(issuanceId, mpToken.MPTokenIssuanceID, "MPTokenIssuanceID should match");

        Console.WriteLine($"MPToken balance: {mpToken.MPTAmount}");
        Assert.IsNotNull(mpToken.MPTAmount, "MPTAmount should not be null after payment");
        Assert.AreEqual(transferAmount, mpToken.MPTAmount.Value, $"MPTAmount should be {transferAmount}");
    }

    #endregion
}
