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
    public async Task TestVaultCreate_WithIOU()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);

        // IOU vault requires DefaultRipple on issuer account
        AccountSet accountSetTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple,
        };
        accountSetTx = await client.Autofill(accountSetTx);
        TransactionSummary setResult = await client.SubmitAndWait(accountSetTx, walletIssuer, true);
        ValidateResult(setResult);

        VaultCreate tx = new VaultCreate
        {
            Account = walletIssuer.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletIssuer, true);
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
        // VaultClawback requires an IOU vault (XRP vaults cannot be clawed back)
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder);

        // 1a. Enable DefaultRipple (required for IOU vault)
        AccountSet defaultRippleTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple,
        };
        defaultRippleTx = await client.Autofill(defaultRippleTx);
        TransactionSummary defaultRippleResult = await client.SubmitAndWait(defaultRippleTx, walletIssuer, true);
        ValidateResult(defaultRippleResult);

        // 1b. Enable clawback on issuer account (must be done before any trust lines)
        AccountSet clawbackTx2 = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback,
        };
        clawbackTx2 = await client.Autofill(clawbackTx2);
        TransactionSummary clawbackSetResult = await client.SubmitAndWait(clawbackTx2, walletIssuer, true);
        ValidateResult(clawbackSetResult);

        // 2. Create IOU vault
        VaultCreate createTx = new VaultCreate
        {
            Account = walletIssuer.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        // 3. Holder sets TrustLine to issuer for USD
        TrustSet trustTx = new TrustSet
        {
            Account = walletHolder.ClassicAddress,
            LimitAmount = new Currency
            {
                Value = "1000",
                CurrencyCode = "USD",
                Issuer = walletIssuer.ClassicAddress,
            },
        };
        trustTx = await client.Autofill(trustTx);
        TransactionSummary trustResult = await client.SubmitAndWait(trustTx, walletHolder, true);
        ValidateResult(trustResult);

        // 4. Issuer sends USD to holder
        Payment paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                Value = "100",
                CurrencyCode = "USD",
                Issuer = walletIssuer.ClassicAddress,
            },
        };
        paymentTx = await client.Autofill(paymentTx);
        TransactionSummary payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(payResult);

        // 5. Holder deposits USD into vault
        VaultDeposit depositTx = new VaultDeposit
        {
            Account = walletHolder.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency
            {
                Value = "50",
                CurrencyCode = "USD",
                Issuer = walletIssuer.ClassicAddress,
            },
        };
        depositTx = await client.Autofill(depositTx);
        TransactionSummary depositResult = await client.SubmitAndWait(depositTx, walletHolder, true);
        ValidateResult(depositResult);

        // 6. Issuer claws back from holder's vault shares
        VaultClawback clawbackTx = new VaultClawback
        {
            Account = walletIssuer.ClassicAddress,
            VaultID = vaultId,
            Holder = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                Value = "50",
                CurrencyCode = "USD",
                Issuer = walletIssuer.ClassicAddress,
            },
        };
        clawbackTx = await client.Autofill(clawbackTx);

        TransactionSummary result = await client.SubmitAndWait(clawbackTx, walletIssuer, true);
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
