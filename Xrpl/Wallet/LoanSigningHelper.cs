using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;

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
        /// Sets SigningPubKey to the broker's public key, removes signature fields,
        /// and optionally adjusts the fee to account for CounterpartySignature overhead.
        /// Returns a JsonObject ready for both parties to sign.
        /// </summary>
        /// <param name="loanSetTx">The LoanSet transaction (autofilled with Sequence, Fee, LastLedgerSequence).</param>
        /// <param name="brokerWallet">The broker's (submitting account's) public key hex.</param>
        /// <param name="adjustFee">If true, triples the fee to account for CounterpartySignature overhead (~150 bytes).</param>
        /// <returns>JsonObject ready for signing by both parties.</returns>
        public static JsonObject PrepareForSigning(
            ITransactionRequest loanSetTx,
            XrplWallet brokerWallet,
            bool adjustFee = true)
        {
            string txJsonStr = JsonSerializer.Serialize(loanSetTx, XrplJsonOptions.Default);
            JsonObject txJson = JsonNode.Parse(txJsonStr)?.AsObject()
                ?? throw new ValidationException("Failed to serialize LoanSet to JSON");

            return PrepareForSigning(txJson, brokerWallet, adjustFee);
        }

        /// <summary>
        /// Prepares a LoanSet JSON object for signing.
        /// </summary>
        public static JsonObject PrepareForSigning(
            JsonObject txJson,
            XrplWallet brokerWallet,
            bool adjustFee = true)
        {
            string txType = txJson["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"TransactionType must be LoanSet, got: {txType}");

            // Adjust fee for CounterpartySignature overhead
            if (adjustFee)
            {
                string feeStr = txJson["Fee"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(feeStr) && ulong.TryParse(feeStr, out ulong feeDrops))
                {
                    feeDrops *= 3; // triple the fee
                    if (feeDrops < 20)
                        feeDrops = 20;
                    txJson["Fee"] = feeDrops.ToString();
                }
            }

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
        {
            // Work on a deep clone to avoid mutating the input
            JsonObject tx = preparedTx.DeepClone().AsObject();

            // Ensure SigningPubKey is set to broker's key
            tx["SigningPubKey"] = brokerWallet.PublicKey;
            tx.Remove("CounterpartySignature");
            tx.Remove("TxnSignature");

            // Compute signing preimage
            byte[] signingBytes = GetSigningPreimage(tx);

            // Borrower signs the preimage
            string counterpartySig = XrplKeypairs.Sign(signingBytes, borrowerWallet.PrivateKey);

            // Add CounterpartySignature
            tx["CounterpartySignature"] = new JsonObject
            {
                ["SigningPubKey"] = borrowerWallet.PublicKey,
                ["TxnSignature"] = counterpartySig,
            };

            // Broker signs the preimage
            string brokerSig = XrplKeypairs.Sign(signingBytes, brokerWallet.PrivateKey);
            tx["TxnSignature"] = brokerSig;

            // Encode the complete signed transaction
            string txBlob = XrplBinaryCodec.Encode(tx);
            string txHash = HashLedger.HashSignedTx(txBlob);
            return new SignatureResult(txBlob, txHash);
        }

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
            JsonObject brokerTx = XrplBinaryCodec.Decode(brokerSignedBlob).AsObject();
            JsonObject counterpartyTx = XrplBinaryCodec.Decode(counterpartySignedBlob).AsObject();

            // Verify both are LoanSet
            string brokerType = brokerTx["TransactionType"]?.GetValue<string>();
            string counterpartyType = counterpartyTx["TransactionType"]?.GetValue<string>();
            if (!string.Equals(brokerType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"Broker blob TransactionType must be LoanSet, got: {brokerType}");
            if (!string.Equals(counterpartyType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"Counterparty blob TransactionType must be LoanSet, got: {counterpartyType}");

            // Verify bodies match (excluding signatures)
            JsonObject brokerCanon = Canonicalize(brokerTx);
            JsonObject counterpartyCanon = Canonicalize(counterpartyTx);
            if (!JsonNode.DeepEquals(brokerCanon, counterpartyCanon))
                throw new ValidationException("Incompatible LoanSet bodies. Both inputs must have identical non-signing fields.");

            // Build combined: start from broker (has TxnSignature + SigningPubKey)
            JsonObject combined = brokerTx.DeepClone().AsObject();

            // Extract CounterpartySignature from borrower's blob
            JsonNode counterpartySigNode = counterpartyTx["CounterpartySignature"];
            if (counterpartySigNode == null)
                throw new ValidationException("Counterparty blob is missing CounterpartySignature.");

            combined["CounterpartySignature"] = counterpartySigNode.DeepClone();

            // Ensure broker's TxnSignature is present
            if (combined["TxnSignature"] == null)
                throw new ValidationException("Broker blob is missing TxnSignature.");

            string txBlob = XrplBinaryCodec.Encode(combined);
            string txHash = HashLedger.HashSignedTx(txBlob);
            return new SignatureResult(txBlob, txHash);
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
            JsonObject tx = XrplBinaryCodec.Decode(partiallySignedBlob).AsObject();

            string txType = tx["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"TransactionType must be LoanSet, got: {txType}");

            // Preserve CounterpartySignature (borrower's partial signature)
            JsonNode counterpartySig = tx["CounterpartySignature"]?.DeepClone()
                ?? throw new ValidationException("Partially signed blob is missing CounterpartySignature.");

            // Strip signatures for preimage computation
            tx.Remove("CounterpartySignature");
            tx.Remove("TxnSignature");
            tx["SigningPubKey"] = brokerWallet.PublicKey;

            // Compute the same preimage the borrower signed
            byte[] signingBytes = GetSigningPreimage(tx);
            string brokerSig = XrplKeypairs.Sign(signingBytes, brokerWallet.PrivateKey);

            // Assemble final tx with both signatures
            tx["TxnSignature"] = brokerSig;
            tx["CounterpartySignature"] = counterpartySig;

            string txBlob = XrplBinaryCodec.Encode(tx);
            string txHash = HashLedger.HashSignedTx(txBlob);
            return new SignatureResult(txBlob, txHash);
        }

        /// <summary>
        /// Computes the signing preimage bytes for a LoanSet transaction.
        /// Both broker and borrower sign the same preimage.
        /// </summary>
        internal static byte[] GetSigningPreimage(JsonObject txJson)
        {
            string signingHex = XrplBinaryCodec.EncodeForSigning(txJson);
            return AddressCodec.Utils.FromHexToBytes(signingHex);
        }

        /// <summary>
        /// Canonicalize a tx by removing all signature-related fields for comparison.
        /// </summary>
        private static JsonObject Canonicalize(JsonObject tx)
        {
            JsonObject canon = tx.DeepClone().AsObject();
            canon.Remove("TxnSignature");
            canon.Remove("SigningPubKey");
            canon.Remove("CounterpartySignature");
            return canon;
        }
    }
}
