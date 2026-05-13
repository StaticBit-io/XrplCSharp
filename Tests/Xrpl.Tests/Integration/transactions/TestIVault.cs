using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
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
    public async Task TestVaultCreate_WithOptionalFields()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        VaultCreate tx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            AssetsMaximum = "10000000000",
            MPTokenMetadata = "48656C6C6F",
            Data = "DEADBEEF",
            Flags = (uint)VaultCreateFlags.tfVaultShareNonTransferable,
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
    public async Task TestVaultWithdraw_WithDestination()
    {
        XrplWallet wallet = XrplWallet.Generate();
        XrplWallet walletDest = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet, walletDest);

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
            Destination = walletDest.ClassicAddress,
            DestinationTag = 42,
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
    public async Task TestVaultSet_UpdateAssetsMaximum()
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
            AssetsMaximum = "50000000000",
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

    [TestMethod]
    public async Task TestVaultLedgerEntry_VerifyFields()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        // Create vault with optional fields to verify deserialization
        VaultCreate createTx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            AssetsMaximum = "50000000000",
            Data = "7B226E223A2254657374205661756C74222C2277223A226578616D706C652E636F6D227D",
            Flags = (uint)VaultCreateFlags.tfVaultShareNonTransferable,
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string vaultId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        // Deposit to set AssetsTotal/AssetsAvailable
        VaultDeposit depositTx = new VaultDeposit
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
        };
        depositTx = await client.Autofill(depositTx);
        TransactionSummary depositResult = await client.SubmitAndWait(depositTx, wallet, true);
        ValidateResult(depositResult);

        // Fetch the Vault LO via ledger_entry
        LedgerEntryRequest entryRequest = new LedgerEntryRequest { Index = vaultId };
        LedgerEntryResponse entryResponse = await client.LedgerEntry(entryRequest);

        Assert.IsNotNull(entryResponse?.Node, "LedgerEntry node should not be null");
        Assert.IsInstanceOfType(entryResponse.Node, typeof(LOVault), "Node should deserialize to LOVault");

        LOVault vault = (LOVault)entryResponse.Node;

        // Verify core fields
        Assert.IsNotNull(vault.Account, "Account (pseudo-account) should be set");
        Assert.AreEqual(wallet.ClassicAddress, vault.Owner, "Owner should match the wallet address");
        Assert.IsNotNull(vault.Asset, "Asset should be set");
        Assert.AreEqual("XRP", vault.Asset.Currency, "Asset currency should be XRP");

        // Verify Number fields
        Assert.IsNotNull(vault.AssetsTotal, "AssetsTotal should be set after deposit");
        Assert.IsNotNull(vault.AssetsAvailable, "AssetsAvailable should be set after deposit");
        Assert.AreEqual("50000000000", vault.AssetsMaximum, "AssetsMaximum should match creation value");

        // Verify ShareMPTID
        Assert.IsNotNull(vault.ShareMPTID, "ShareMPTID should be set");

        // Verify Data parsing
        Assert.IsNotNull(vault.Data, "Data should be set");
        Assert.IsNotNull(vault.DataParsed, "DataParsed should parse successfully");
        Assert.AreEqual("Test Vault", vault.DataParsed.Name, "DataParsed.Name should match");
        Assert.AreEqual("example.com", vault.DataParsed.Website, "DataParsed.Website should match");
        Assert.IsNotNull(vault.DataRaw, "DataRaw should decode successfully");

        // Verify infrastructure fields
        Assert.IsNotNull(vault.Sequence, "Sequence should be set");
        Assert.IsNotNull(vault.OwnerNode, "OwnerNode should be set");
        Assert.IsNotNull(vault.PreviousTxnID, "PreviousTxnID should be set");
        Assert.IsNotNull(vault.PreviousTxnLgrSeq, "PreviousTxnLgrSeq should be set");
    }

    [TestMethod]
    public async Task TestVaultClawback_MPT()
    {
        // VaultClawback with MPT-backed vault
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder);

        // 1. Enable clawback on issuer (must be before any issuances)
        AccountSet clawbackSetTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback,
        };
        clawbackSetTx = await client.Autofill(clawbackSetTx);
        TransactionSummary clawbackSetResult = await client.SubmitAndWait(clawbackSetTx, walletIssuer, true);
        ValidateResult(clawbackSetResult);

        // 2. Create MPT issuance with clawback enabled
        MPTokenIssuanceCreate mptCreateTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer | MPTokenIssuanceCreateFlags.tfMPTCanClawback,
        };
        mptCreateTx = await client.Autofill(mptCreateTx);
        TransactionSummary mptCreateResult = await client.SubmitAndWait(mptCreateTx, walletIssuer, true);
        ValidateResult(mptCreateResult);

        string issuanceId = mptCreateResult.Meta?.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be present in metadata");

        // 3. Holder authorizes MPT
        MPTokenAuthorize authTx = new MPTokenAuthorize
        {
            Account = walletHolder.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        TransactionSummary authResult = await client.SubmitAndWait(authTx, walletHolder, true);
        ValidateResult(authResult);

        // 4. Issuer sends MPT to holder
        Payment paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                Value = "100",
                MPTokenIssuanceID = issuanceId,
            },
        };
        paymentTx = await client.Autofill(paymentTx);
        TransactionSummary payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(payResult);

        // 5. Create MPT-backed vault
        VaultCreate vaultCreateTx = new VaultCreate
        {
            Account = walletIssuer.ClassicAddress,
            Asset = new IssuedCurrency { MptIssuanceId = issuanceId },
        };
        vaultCreateTx = await client.Autofill(vaultCreateTx);
        TransactionSummary vaultCreateResult = await client.SubmitAndWait(vaultCreateTx, walletIssuer, true);
        ValidateResult(vaultCreateResult);

        string vaultId = GetCreatedObjectId(vaultCreateResult);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata");

        // 6. Holder deposits MPT into vault
        VaultDeposit depositTx = new VaultDeposit
        {
            Account = walletHolder.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency
            {
                Value = "50",
                MPTokenIssuanceID = issuanceId,
            },
        };
        depositTx = await client.Autofill(depositTx);
        TransactionSummary depositResult = await client.SubmitAndWait(depositTx, walletHolder, true);
        ValidateResult(depositResult);

        // 7. Issuer claws back from holder's vault shares
        VaultClawback clawbackTx = new VaultClawback
        {
            Account = walletIssuer.ClassicAddress,
            VaultID = vaultId,
            Holder = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                Value = "50",
                MPTokenIssuanceID = issuanceId,
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
