using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

namespace Xrpl.Wallet
{
    /// <summary>
    /// Helper for LoanSet multi-party signing (XLS-66d).
    /// LoanSet requires two signatures: the broker (Account) signs as the submitter (TxnSignature),
    /// and the borrower (Counterparty) provides a CounterpartySignature (inner STObject with
    /// SigningPubKey + TxnSignature).
    ///
    /// Three signing patterns (analogous to Batch V1/V2/V3):
    ///
    /// <b>V1 — Automatic (both keys available):</b>
    /// <code>
    /// var result = LoanSigningHelper.SignLoanSet(loanTx, brokerWallet, borrowerWallet);
    /// await client.SubmitRequest(result.TxBlob);
    /// </code>
    ///
    /// <b>V2 — Parallel (keys on separate devices, sign independently):</b>
    /// <code>
    /// // Device A (borrower):
    /// var counterpartySig = borrowerWallet.SignAsLoanCounterparty(preparedTxJson);
    /// // Device B (broker):
    /// var brokerSig = brokerWallet.Sign(preparedTxJson);
    /// // Combiner:
    /// var combined = LoanSigningHelper.CombineLoanSignatures(brokerSig.TxBlob, counterpartySig.TxBlob);
    /// await client.SubmitRequest(combined);
    /// </code>
    ///
    /// <b>V3 — Sequential (borrower signs first, passes to broker):</b>
    /// <code>
    /// // Borrower signs, adds CounterpartySignature:
    /// var withCounterparty = borrowerWallet.SignAsLoanCounterparty(preparedTxJson);
    /// // Broker receives the partially signed blob, adds TxnSignature:
    /// var final = LoanSigningHelper.BrokerSign(withCounterparty.TxBlob, brokerWallet);
    /// await client.SubmitRequest(final.TxBlob);
    /// </code>
    /// </summary>
    public static class LoanSigningHelper
    {
        /// <summary>
        /// Prepares a LoanSet transaction JSON for signing.
        /// Sets SigningPubKey to the broker's public key and removes signature fields.
        /// Returns a JsonObject ready for both parties to sign.
        /// </summary>
        /// <remarks>
        /// Fee for CounterpartySignature overhead is handled by Autofill
        /// (see <c>CalculateBaseFeeForType</c> in <c>Autofill.cs</c>).
        /// </remarks>
        /// <param name="loanSetTx">The LoanSet transaction (autofilled with Sequence, Fee, LastLedgerSequence).</param>
        /// <param name="brokerWallet">The broker's wallet (submitting account).</param>
        /// <returns>JsonObject ready for signing by both parties.</returns>
        public static JsonObject PrepareForSigning(
            ITransactionRequest loanSetTx,
            XrplWallet brokerWallet)
        {
            string txJsonStr = JsonSerializer.Serialize(loanSetTx, XrplJsonOptions.Default);
            JsonObject txJson = JsonNode.Parse(txJsonStr)?.AsObject()
                ?? throw new ValidationException("Failed to serialize LoanSet to JSON");

            return PrepareForSigning(txJson, brokerWallet);
        }

        /// <summary>
        /// Prepares a LoanSet JSON object for signing.
        /// </summary>
        public static JsonObject PrepareForSigning(
            JsonObject txJson,
            XrplWallet brokerWallet)
        {
            string txType = txJson["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"TransactionType must be LoanSet, got: {txType}");

            // Set broker's signing pub key
            txJson["SigningPubKey"] = brokerWallet.PublicKey;

            // Remove signature fields
            txJson.Remove("CounterpartySignature");
            txJson.Remove("TxnSignature");

            return txJson;
        }

        /// <summary>
        /// V1 — Automatic signing: both broker and borrower wallets available locally.
        /// Computes the signing preimage, has both parties sign, and returns the fully signed tx blob.
        /// </summary>
        /// <param name="preparedTx">Prepared LoanSet JSON (from PrepareForSigning or already prepared).</param>
        /// <param name="brokerWallet">The broker's wallet (submitting account).</param>
        /// <param name="borrowerWallet">The borrower's wallet (counterparty).</param>
        /// <returns>Fully signed transaction blob and hash.</returns>
        public static SignatureResult SignLoanSet(
            JsonObject preparedTx,
            XrplWallet brokerWallet,
            XrplWallet borrowerWallet)
            => CoSigningEngine.SignBoth(preparedTx, brokerWallet, borrowerWallet, "CounterpartySignature");

        /// <summary>
        /// V2 — Combine independently signed broker and counterparty blobs.
        /// The broker blob has TxnSignature but no CounterpartySignature.
        /// The counterparty blob has CounterpartySignature but no TxnSignature.
        /// </summary>
        /// <param name="brokerSignedBlob">Hex blob signed by the broker (has TxnSignature).</param>
        /// <param name="counterpartySignedBlob">Hex blob signed by the borrower (has CounterpartySignature).</param>
        /// <returns>Combined fully signed blob.</returns>
        public static SignatureResult CombineLoanSignatures(
            string brokerSignedBlob,
            string counterpartySignedBlob)
        {
            RequireLoanSet(brokerSignedBlob, "Broker");
            RequireLoanSet(counterpartySignedBlob, "Counterparty");
            return CoSigningEngine.Combine(brokerSignedBlob, counterpartySignedBlob, "CounterpartySignature", "Counterparty");
        }

        /// <summary>
        /// V3 — Broker signs a partially signed LoanSet blob (one that already has CounterpartySignature).
        /// Decodes the blob, strips CounterpartySignature to compute the correct preimage,
        /// adds the broker's TxnSignature, then restores CounterpartySignature for encoding.
        /// </summary>
        /// <param name="partiallySignedBlob">Hex blob from borrower's SignAsLoanCounterparty (has CounterpartySignature, no TxnSignature).</param>
        /// <param name="brokerWallet">The broker's wallet (submitting account).</param>
        /// <returns>Fully signed transaction blob and hash.</returns>
        public static SignatureResult BrokerSign(string partiallySignedBlob, XrplWallet brokerWallet)
        {
            RequireLoanSet(partiallySignedBlob, "Partially signed");
            return CoSigningEngine.FinalizeAsSubmitter(partiallySignedBlob, brokerWallet, "CounterpartySignature");
        }

        private static void RequireLoanSet(string blob, string label)
        {
            JsonObject tx = XrplBinaryCodec.Decode(blob).AsObject();
            string txType = tx["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"{label} blob TransactionType must be LoanSet, got: {txType}");
        }

        /// <summary>
        /// Computes the signing preimage bytes for a LoanSet transaction.
        /// Both broker and borrower sign the same preimage.
        /// </summary>
        internal static byte[] GetSigningPreimage(JsonObject txJson)
            => CoSigningEngine.GetSigningPreimage(txJson);

    }
}
