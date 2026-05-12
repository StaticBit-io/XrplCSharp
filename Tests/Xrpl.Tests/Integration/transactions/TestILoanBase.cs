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
    /// Signs and submits a LoanSet transaction with CounterpartySignature.
    /// LoanSet requires the borrower (Counterparty) to co-sign via CounterpartySignature.
    /// Flow:
    ///   1. Autofill the transaction
    ///   2. Compute the signing preimage
    ///   3. Borrower signs the preimage → CounterpartySignature
    ///   4. Broker signs the preimage → TxnSignature
    ///   5. Submit and wait
    /// </summary>
    protected static async Task<TransactionSummary> SubmitLoanSetWithCounterpartySig(
        IXrplClient client,
        LoanSet loanTx,
        XrplWallet brokerWallet,
        XrplWallet borrowerWallet)
    {
        // Autofill sequence, fee, lastLedgerSequence
        loanTx = await client.Autofill(loanTx);

        // Increase Fee to account for CounterpartySignature overhead (~150 bytes extra)
        // Autofill calculates fee based on the tx WITHOUT CounterpartySignature
        if (loanTx.Fee != null)
        {
            ulong feeDrops = ulong.Parse(loanTx.Fee.Value);
            feeDrops = feeDrops * 3; // triple the fee to be safe
            if (feeDrops < 20)
                feeDrops = 20;
            loanTx.Fee = new Currency { Value = feeDrops.ToString(), CurrencyCode = "XRP" };
        }

        // Convert model → JSON for the codec
        string txJsonStr = JsonSerializer.Serialize(loanTx, XrplJsonOptions.Default);
        JsonObject txJson = JsonNode.Parse(txJsonStr)?.AsObject();

        // Set broker's signing pub key
        txJson["SigningPubKey"] = brokerWallet.PublicKey;

        // Remove fields not part of signing preimage
        txJson.Remove("CounterpartySignature");
        txJson.Remove("TxnSignature");

        // Compute signing preimage (same for both broker and borrower)
        string signingHex = XrplBinaryCodec.EncodeForSigning(txJson);
        byte[] signingBytes = global::Xrpl.AddressCodec.Utils.FromHexToBytes(signingHex);

        // Borrower signs the preimage
        string counterpartySig = global::Xrpl.Keypairs.XrplKeypairs.Sign(signingBytes, borrowerWallet.PrivateKey);

        // Add CounterpartySignature
        txJson["CounterpartySignature"] = new JsonObject
        {
            ["SigningPubKey"] = borrowerWallet.PublicKey,
            ["TxnSignature"] = counterpartySig,
        };

        // Broker signs the preimage
        string brokerSig = global::Xrpl.Keypairs.XrplKeypairs.Sign(signingBytes, brokerWallet.PrivateKey);
        txJson["TxnSignature"] = brokerSig;

        // Encode the complete signed transaction
        string txBlob = XrplBinaryCodec.Encode(txJson);

        // Submit
        Submit submitResult = await client.SubmitRequest(txBlob, failHard: false);
        if (submitResult is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"LoanSet submit failed: {submitResult.EngineResult} - {submitResult.EngineResultMessage}");

        // Wait for ledger to close and get the result via tx lookup
        string txHash = global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(txBlob);
        await Task.Delay(5000);

        TxRequest txReq = new TxRequest(txHash);
        TransactionResponse txResponse = await client.Tx(txReq);

        // Convert TransactionResponse to TransactionSummary-like result for consistency
        // We just need Meta for extracting created object IDs
        return new TransactionSummary { Meta = txResponse?.Meta };
    }

    protected static async Task<IXrplClient> CreateStandaloneClient()
    {
        return await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }
}
