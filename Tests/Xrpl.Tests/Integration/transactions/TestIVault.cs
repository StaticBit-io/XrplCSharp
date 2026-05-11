using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("Vault")]
public class TestIVault : TestIVaultBase
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
    public async Task TestVaultCreate_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate tx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultCreate_WithAsset2()
    {
        XrplWallet wallet = XrplWallet.Generate();
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet, walletIssuer);

        VaultCreate tx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultCreate_WithData()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate tx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Data = "48656C6C6F",
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultDeposit_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate createTx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        VaultDeposit depositTx = new VaultDeposit
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
        };
        depositTx = await client.Autofill(depositTx);

        TransactionSummary result = await client.SubmitAndWait(depositTx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultWithdraw_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate createTx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        VaultDeposit depositTx = new VaultDeposit
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
        };
        depositTx = await client.Autofill(depositTx);
        TransactionSummary depositResult = await client.SubmitAndWait(depositTx, wallet, true);
        ValidateResult(depositResult);

        VaultWithdraw withdrawTx = new VaultWithdraw
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "500000", CurrencyCode = "XRP" },
        };
        withdrawTx = await client.Autofill(withdrawTx);

        TransactionSummary result = await client.SubmitAndWait(withdrawTx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultSet_UpdateData()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate createTx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        VaultSet setTx = new VaultSet
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Data = "55706461746564",
        };
        setTx = await client.Autofill(setTx);

        TransactionSummary result = await client.SubmitAndWait(setTx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultDelete_Empty()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate createTx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        VaultDelete deleteTx = new VaultDelete
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
        };
        deleteTx = await client.Autofill(deleteTx);

        TransactionSummary result = await client.SubmitAndWait(deleteTx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestVaultClawback_Basic()
    {
        XrplWallet walletOwner = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletOwner, walletHolder);

        VaultCreate createTx = new VaultCreate
        {
            Account = walletOwner.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, walletOwner, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        VaultClawback clawbackTx = new VaultClawback
        {
            Account = walletOwner.ClassicAddress,
            VaultID = vaultId,
            Holder = walletHolder.ClassicAddress,
        };
        clawbackTx = await client.Autofill(clawbackTx);

        TransactionSummary result = await client.SubmitAndWait(clawbackTx, walletOwner, true);
        ValidateResult(result);
    }

    private static string GetCreatedObjectId(TransactionSummary result, LedgerEntryType entryType = LedgerEntryType.Vault)
    {
        if (result.Meta?.AffectedNodes == null) return null;

        foreach (AffectedNode node in result.Meta.AffectedNodes)
        {
            if (node.CreatedNode is { } created && created.LedgerEntryType == entryType)
            {
                return created.LedgerIndex;
            }
        }
        return null;
    }
}
