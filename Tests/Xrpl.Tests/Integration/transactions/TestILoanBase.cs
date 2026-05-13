using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

public abstract class TestILoanBase
{
    public TestContext TestContext { get; set; }
    protected abstract IXrplClient GetClient();
    protected static TestNodeType nodeType = TestNodeType.Standalone;

    protected static void ValidateResult(Submit res)
    {
        if (res is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"Transaction failed: {res.EngineResult}");
    }

    protected static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Final tx result is not success: {res.Meta?.TransactionResult}");
    }

    protected static string GetCreatedObjectId(TransactionSummary result, LedgerEntryType entryType = LedgerEntryType.Unknown)
    {
        if (result.Meta?.AffectedNodes == null) return null;

        foreach (AffectedNode node in result.Meta.AffectedNodes)
        {
            if (node.CreatedNode != null)
            {
                if (entryType == LedgerEntryType.Unknown || node.CreatedNode.LedgerEntryType == entryType)
                    return node.CreatedNode.LedgerIndex;
            }
        }
        return null;
    }

    protected static string GetCreatedObjectId(TransactionResponse result, LedgerEntryType entryType = LedgerEntryType.Unknown)
    {
        if (result.Meta?.AffectedNodes == null) return null;

        foreach (AffectedNode node in result.Meta.AffectedNodes)
        {
            if (node.CreatedNode != null)
            {
                if (entryType == LedgerEntryType.Unknown || node.CreatedNode.LedgerEntryType == entryType)
                    return node.CreatedNode.LedgerIndex;
            }
        }
        return null;
    }

    /// <summary>
    /// Creates a Vault for the given wallet and returns its VaultID from metadata.
    /// LoanBrokerSet requires an existing Vault owned by the submitting account.
    /// </summary>
    protected static async Task<string> CreateVaultForBroker(IXrplClient client, XrplWallet wallet)
    {
        VaultCreate vaultTx = new VaultCreate
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        };
        vaultTx = await client.Autofill(vaultTx);
        TransactionSummary vaultResult = await client.SubmitAndWait(vaultTx, wallet, true);
        ValidateResult(vaultResult);

        string vaultId = GetCreatedObjectId(vaultResult, LedgerEntryType.Vault);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata after VaultCreate");
        return vaultId;
    }

    /// <summary>
    /// Creates a LoanBroker for the given wallet (creating a Vault first, depositing funds,
    /// and creating broker cover) and returns the LoanBrokerID.
    /// </summary>
    protected static async Task<string> CreateBroker(IXrplClient client, XrplWallet wallet)
    {
        string vaultId = await CreateVaultForBroker(client, wallet);

        // Deposit XRP into the vault so the broker has funds to lend
        VaultDeposit depositTx = new VaultDeposit
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "100000000", CurrencyCode = "XRP" }, // 100 XRP
        };
        depositTx = await client.Autofill(depositTx);
        TransactionSummary depositResult = await client.SubmitAndWait(depositTx, wallet, true);
        ValidateResult(depositResult);

        LoanBrokerSet brokerTx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
        };
        brokerTx = await client.Autofill(brokerTx);
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, wallet, true);
        ValidateResult(brokerResult);

        string brokerId = GetCreatedObjectId(brokerResult, LedgerEntryType.LoanBroker);
        Assert.IsNotNull(brokerId, "LoanBrokerID should be present in metadata");

        // Deposit cover so the broker can issue loans
        LoanBrokerCoverDeposit coverTx = new LoanBrokerCoverDeposit
        {
            Account = wallet.ClassicAddress,
            LoanBrokerID = brokerId,
            Amount = new Currency { Value = "50000000", CurrencyCode = "XRP" }, // 50 XRP
        };
        coverTx = await client.Autofill(coverTx);
        TransactionSummary coverResult = await client.SubmitAndWait(coverTx, wallet, true);
        ValidateResult(coverResult);

        return brokerId;
    }

    /// <summary>
    /// Autofills and prepares a LoanSet transaction for co-signing.
    /// Returns the prepared JsonObject with SigningPubKey set.
    /// Fee for CounterpartySignature overhead is handled by Autofill.
    /// </summary>
    protected static async Task<JsonObject> PrepareLoanSet(
        IXrplClient client,
        LoanSet loanTx,
        XrplWallet brokerWallet)
    {
        loanTx = await client.Autofill(loanTx);
        return LoanSigningHelper.PrepareForSigning(loanTx, brokerWallet);
    }

    /// <summary>
    /// V1 — Automatic: both keys available locally.
    /// Signs and submits a LoanSet transaction using LoanSigningHelper.
    /// </summary>
    protected static async Task<TransactionSummary> SubmitLoanSetV1(
        IXrplClient client,
        LoanSet loanTx,
        XrplWallet brokerWallet,
        XrplWallet borrowerWallet)
    {
        JsonObject prepared = await PrepareLoanSet(client, loanTx, brokerWallet);
        SignatureResult result = LoanSigningHelper.SignLoanSet(prepared, brokerWallet, borrowerWallet);
        return await SubmitSignedLoanSet(client, result.TxBlob);
    }

    /// <summary>
    /// V2 — Parallel: broker and borrower sign independently, then combine.
    /// Simulates keys on separate devices.
    /// </summary>
    protected static async Task<TransactionSummary> SubmitLoanSetV2(
        IXrplClient client,
        LoanSet loanTx,
        XrplWallet brokerWallet,
        XrplWallet borrowerWallet)
    {
        JsonObject prepared = await PrepareLoanSet(client, loanTx, brokerWallet);
        string preparedJson = prepared.ToJsonString();

        // Device A (broker): signs the transaction normally (adds TxnSignature)
        Dictionary<string, object> brokerDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
            preparedJson, XrplJsonOptions.Default);
        SignatureResult brokerSig = brokerWallet.Sign(brokerDict);

        // Device B (borrower): signs as counterparty (adds CounterpartySignature) — independent copy
        Dictionary<string, object> borrowerDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
            preparedJson, XrplJsonOptions.Default);
        SignatureResult counterpartySig = borrowerWallet.SignAsLoanCounterparty(borrowerDict);

        // Combiner: merge both signatures
        SignatureResult combined = LoanSigningHelper.CombineLoanSignatures(brokerSig.TxBlob, counterpartySig.TxBlob);
        return await SubmitSignedLoanSet(client, combined.TxBlob);
    }

    /// <summary>
    /// V3 — Sequential: borrower signs first, passes to broker who signs and submits.
    /// Simulates real-world flow where keys never leave their respective devices.
    /// </summary>
    protected static async Task<TransactionSummary> SubmitLoanSetV3(
        IXrplClient client,
        LoanSet loanTx,
        XrplWallet brokerWallet,
        XrplWallet borrowerWallet)
    {
        JsonObject prepared = await PrepareLoanSet(client, loanTx, brokerWallet);

        // Step 1: Borrower receives prepared tx, signs as counterparty (adds CounterpartySignature)
        Dictionary<string, object> txDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
            prepared.ToJsonString(), XrplJsonOptions.Default);
        SignatureResult withCounterparty = borrowerWallet.SignAsLoanCounterparty(txDict);

        // Step 2: Broker receives the partially signed blob, adds TxnSignature via BrokerSign
        SignatureResult fullySigned = LoanSigningHelper.BrokerSign(withCounterparty.TxBlob, brokerWallet);

        return await SubmitSignedLoanSet(client, fullySigned.TxBlob);
    }

    /// <summary>
    /// Default method (backward compatible) — uses V1 (automatic) signing.
    /// </summary>
    protected static Task<TransactionSummary> SubmitLoanSetWithCounterpartySig(
        IXrplClient client,
        LoanSet loanTx,
        XrplWallet brokerWallet,
        XrplWallet borrowerWallet)
        => SubmitLoanSetV1(client, loanTx, brokerWallet, borrowerWallet);

    /// <summary>
    /// Submits a signed LoanSet blob and waits for the result.
    /// </summary>
    protected static async Task<TransactionSummary> SubmitSignedLoanSet(IXrplClient client, string txBlob)
    {
        Submit submitResult = await client.SubmitRequest(txBlob, failHard: false);
        if (submitResult is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"LoanSet submit failed: {submitResult.EngineResult} - {submitResult.EngineResultMessage}");

        // Poll tx lookup until metadata is available (ledger_accept runs every 4s in CI)
        string txHash = global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(txBlob);
        TxRequest txReq = new TxRequest(txHash);
        TransactionResponse txResponse = null;
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(1000);
            try
            {
                txResponse = await client.Tx(txReq);
                if (txResponse?.Meta != null) break;
            }
            catch
            {
                // tx may not be found yet — retry
            }
        }
        if (txResponse?.Meta == null)
            throw new RippleException($"LoanSet tx not validated in time: {txHash}");

        return new TransactionSummary { Meta = txResponse.Meta };
    }

    /// <summary>
    /// Creates an MPT issuance, authorizes it for the holder, funds the holder with MPT tokens,
    /// creates an MPT-backed Vault, deposits MPT into it, creates a LoanBroker backed by that vault,
    /// deposits cover, and returns the broker ID along with the MPT issuance ID.
    /// </summary>
    protected static async Task<(string BrokerId, string MptIssuanceId)> CreateMptBroker(
        IXrplClient client,
        XrplWallet issuerWallet,
        XrplWallet holderWallet)
    {
        // 1. Enable clawback on issuer (required before any issuances for clawback support)
        AccountSet clawbackSetTx = new AccountSet
        {
            Account = issuerWallet.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback,
        };
        clawbackSetTx = await client.Autofill(clawbackSetTx);
        TransactionSummary clawbackSetResult = await client.SubmitAndWait(clawbackSetTx, issuerWallet, true);
        ValidateResult(clawbackSetResult);

        // 2. Create MPT issuance with transfer + clawback enabled
        MPTokenIssuanceCreate mptCreateTx = new MPTokenIssuanceCreate
        {
            Account = issuerWallet.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer | MPTokenIssuanceCreateFlags.tfMPTCanClawback,
        };
        mptCreateTx = await client.Autofill(mptCreateTx);
        TransactionSummary mptCreateResult = await client.SubmitAndWait(mptCreateTx, issuerWallet, true);
        ValidateResult(mptCreateResult);

        string issuanceId = mptCreateResult.Meta?.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be present in metadata");

        // 3. Holder authorizes MPT
        MPTokenAuthorize authTx = new MPTokenAuthorize
        {
            Account = holderWallet.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        TransactionSummary authResult = await client.SubmitAndWait(authTx, holderWallet, true);
        ValidateResult(authResult);

        // 4. Issuer sends MPT to holder (so holder can deposit to vault later)
        Payment paymentTx = new Payment
        {
            Account = issuerWallet.ClassicAddress,
            Destination = holderWallet.ClassicAddress,
            Amount = new Currency
            {
                Value = "500",
                MPTokenIssuanceID = issuanceId,
            },
        };
        paymentTx = await client.Autofill(paymentTx);
        TransactionSummary payResult = await client.SubmitAndWait(paymentTx, issuerWallet, true);
        ValidateResult(payResult);

        // 5. Create MPT-backed vault
        VaultCreate vaultCreateTx = new VaultCreate
        {
            Account = issuerWallet.ClassicAddress,
            Asset = new IssuedCurrency { MptIssuanceId = issuanceId },
        };
        vaultCreateTx = await client.Autofill(vaultCreateTx);
        TransactionSummary vaultCreateResult = await client.SubmitAndWait(vaultCreateTx, issuerWallet, true);
        ValidateResult(vaultCreateResult);

        string vaultId = GetCreatedObjectId(vaultCreateResult, LedgerEntryType.Vault);
        Assert.IsNotNull(vaultId, "VaultID should be present in metadata after VaultCreate");

        // 6. Deposit MPT into the vault so it has liquidity
        VaultDeposit depositTx = new VaultDeposit
        {
            Account = holderWallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency
            {
                Value = "200",
                MPTokenIssuanceID = issuanceId,
            },
        };
        depositTx = await client.Autofill(depositTx);
        TransactionSummary depositResult = await client.SubmitAndWait(depositTx, holderWallet, true);
        ValidateResult(depositResult);

        // 7. Create LoanBroker backed by MPT vault
        LoanBrokerSet brokerTx = new LoanBrokerSet
        {
            Account = issuerWallet.ClassicAddress,
            VaultID = vaultId,
        };
        brokerTx = await client.Autofill(brokerTx);
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, issuerWallet, true);
        ValidateResult(brokerResult);

        string brokerId = GetCreatedObjectId(brokerResult, LedgerEntryType.LoanBroker);
        Assert.IsNotNull(brokerId, "LoanBrokerID should be present in metadata");

        // 8. Deposit cover (MPT) into the broker
        LoanBrokerCoverDeposit coverTx = new LoanBrokerCoverDeposit
        {
            Account = issuerWallet.ClassicAddress,
            LoanBrokerID = brokerId,
            Amount = new Currency
            {
                Value = "100",
                MPTokenIssuanceID = issuanceId,
            },
        };
        coverTx = await client.Autofill(coverTx);
        TransactionSummary coverResult = await client.SubmitAndWait(coverTx, issuerWallet, true);
        ValidateResult(coverResult);

        return (brokerId, issuanceId);
    }

    protected static async Task<IXrplClient> CreateStandaloneClient()
    {
        return await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }
}
