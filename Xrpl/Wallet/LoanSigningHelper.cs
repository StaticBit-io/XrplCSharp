using System;
using System.Collections.Generic;
using System.Linq;
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
    /// LoanSet carries two signatures: the submitter (<c>Account</c>) signs the transaction itself
    /// (TxnSignature), and the counterparty provides a CounterpartySignature (inner STObject with
    /// SigningPubKey + TxnSignature, or Signers).
    ///
    /// The roles are not tied to broker and borrower. The counterparty is the <c>Counterparty</c>
    /// field, or the LoanBroker's owner when the field is omitted; one of <c>Account</c> and the
    /// counterparty must be the broker owner, and the other one is the borrower (rippled
    /// <c>LoanSet::preclaim</c> and <c>LoanSet::checkSign</c>). Either the broker owner submits and
    /// the borrower co-signs, or the borrower submits and the broker owner co-signs.
    ///
    /// Three signing patterns (analogous to Batch V1/V2/V3):
    ///
    /// <b>V1 — Automatic (both keys available):</b>
    /// <code>
    /// var result = LoanSigningHelper.SignLoanSet(loanTx, submitterWallet, counterpartyWallet);
    /// await client.SubmitRequest(result.TxBlob);
    /// </code>
    ///
    /// <b>V2 — Parallel (keys on separate devices, sign independently):</b>
    /// <code>
    /// // Device A (counterparty):
    /// var counterpartySig = counterpartyWallet.SignAsLoanCounterparty(preparedTxJson);
    /// // Device B (submitter):
    /// var submitterSig = submitterWallet.Sign(preparedTxJson);
    /// // Combiner:
    /// var combined = LoanSigningHelper.CombineLoanSignatures(submitterSig.TxBlob, counterpartySig.TxBlob);
    /// await client.SubmitRequest(combined);
    /// </code>
    ///
    /// <b>V3 — Sequential (counterparty signs first, passes to the submitter):</b>
    /// <code>
    /// // Counterparty signs, adds CounterpartySignature:
    /// var withCounterparty = counterpartyWallet.SignAsLoanCounterparty(preparedTxJson);
    /// // Submitter receives the partially signed blob, adds TxnSignature:
    /// var final = LoanSigningHelper.SubmitterSign(withCounterparty.TxBlob, submitterWallet);
    /// await client.SubmitRequest(final.TxBlob);
    /// </code>
    ///
    /// <b>Multisig counterparty (a counterparty with a SignerList):</b> each signer of the
    /// counterparty's list produces a portable Signer entry with the standard multisign
    /// call, and the composer places them under <c>CounterpartySignature.Signers</c>
    /// (rippled verifies them against the counterparty's SignerList over the same
    /// multisign preimage as <c>tx.Signers</c>). Autofill with the signer count so the
    /// fee covers them (rippled <c>LoanSet::calculateBaseFee</c> charges one base fee
    /// per counterparty signer):
    /// <code>
    /// var prepared = LoanSigningHelper.PrepareForSigning(await client.Autofill(loanTx.ToDictionary(), signersCount: 2), submitterWallet);
    /// var submitterPart = submitterWallet.Sign(prepared);
    /// var part1 = signer1.Sign(prepared, multisign: true);
    /// var part2 = signer2.Sign(prepared, multisign: true);
    /// // ledger-driven routing (looks up the counterparty's SignerList):
    /// var composed = await client.ComposeSignatures(new[] { submitterPart.TxBlob, part1.TxBlob, part2.TxBlob });
    /// // or offline, naming the counterparty's signers:
    /// var offline = LoanSigningHelper.CombineLoanSignatures(new[] { submitterPart.TxBlob, part1.TxBlob, part2.TxBlob }, new[] { signer1.ClassicAddress, signer2.ClassicAddress });
    /// </code>
    /// </summary>
    public static class LoanSigningHelper
    {
        /// <summary>
        /// Prepares a LoanSet transaction JSON for signing.
        /// Sets SigningPubKey to the submitter's public key and removes signature fields.
        /// Returns a JsonObject ready for both parties to sign.
        /// </summary>
        /// <remarks>
        /// Fee for CounterpartySignature overhead is handled by Autofill
        /// (see <c>CalculateBaseFeeForType</c> in <c>Autofill.cs</c>).
        /// </remarks>
        /// <param name="loanSetTx">The LoanSet transaction (autofilled with Sequence, Fee, LastLedgerSequence).</param>
        /// <param name="submitterWallet">The wallet of the submitting account (<c>Account</c>): the broker owner or the borrower.</param>
        /// <returns>JsonObject ready for signing by both parties.</returns>
        public static JsonObject PrepareForSigning(
            ITransactionRequest loanSetTx,
            XrplWallet submitterWallet)
        {
            string txJsonStr = JsonSerializer.Serialize(loanSetTx, XrplJsonOptions.Default);
            JsonObject txJson = JsonNode.Parse(txJsonStr)?.AsObject()
                ?? throw new ValidationException("Failed to serialize LoanSet to JSON");

            return PrepareForSigning(txJson, submitterWallet);
        }

        /// <summary>
        /// Prepares a LoanSet JSON object for signing.
        /// </summary>
        /// <param name="txJson">The LoanSet transaction JSON.</param>
        /// <param name="submitterWallet">The wallet of the submitting account (<c>Account</c>): the broker owner or the borrower.</param>
        public static JsonObject PrepareForSigning(
            JsonObject txJson,
            XrplWallet submitterWallet)
        {
            string txType = txJson["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"TransactionType must be LoanSet, got: {txType}");

            txJson["SigningPubKey"] = submitterWallet.PublicKey;

            txJson.Remove("CounterpartySignature");
            txJson.Remove("TxnSignature");

            return txJson;
        }

        /// <summary>
        /// V1 — Automatic signing: both wallets available locally.
        /// Computes the signing preimages, has both parties sign, and returns the fully signed tx blob.
        /// </summary>
        /// <remarks>
        /// The two wallets may be passed in either order: the one whose address is <c>Account</c>
        /// signs the transaction (TxnSignature), the other signs CounterpartySignature. The other
        /// wallet must be the counterparty: the <c>Counterparty</c> field when present. When the
        /// field is omitted the node takes the LoanBroker's owner as the counterparty, which this
        /// offline overload cannot check; pass the owner through
        /// <see cref="SignLoanSet(JsonObject, XrplWallet, XrplWallet, string)"/> to have it checked
        /// here rather than rejected by the node as an invalid counterparty signature.
        /// </remarks>
        /// <param name="preparedTx">Prepared LoanSet JSON (from PrepareForSigning or already prepared).</param>
        /// <param name="submitterWallet">The submitting account's wallet (<c>Account</c>).</param>
        /// <param name="counterpartyWallet">The counterparty's wallet.</param>
        /// <returns>Fully signed transaction blob and hash.</returns>
        public static SignatureResult SignLoanSet(
            JsonObject preparedTx,
            XrplWallet submitterWallet,
            XrplWallet counterpartyWallet)
            => SignLoanSet(preparedTx, submitterWallet, counterpartyWallet, null);

        /// <summary>
        /// V1 — Automatic signing with the LoanBroker's owner known, so the roles are checked the way
        /// rippled checks them before anything is signed.
        /// </summary>
        /// <remarks>
        /// The counterparty is <c>Counterparty</c>, or <paramref name="brokerOwner"/> when the field
        /// is omitted (rippled <c>LoanSet::checkSign</c>), and one of <c>Account</c> and the
        /// counterparty must be the broker owner (rippled <c>LoanSet::preclaim</c>,
        /// <c>tecNO_PERMISSION</c> otherwise). The wallets may be passed in either order. The owner
        /// is the <c>Owner</c> of the LoanBroker ledger entry named by <c>LoanBrokerID</c>.
        /// </remarks>
        /// <param name="preparedTx">Prepared LoanSet JSON (from PrepareForSigning or already prepared).</param>
        /// <param name="submitterWallet">The submitting account's wallet (<c>Account</c>).</param>
        /// <param name="counterpartyWallet">The counterparty's wallet.</param>
        /// <param name="brokerOwner">The LoanBroker's owner, or null to skip the owner checks.</param>
        /// <returns>Fully signed transaction blob and hash.</returns>
        public static SignatureResult SignLoanSet(
            JsonObject preparedTx,
            XrplWallet submitterWallet,
            XrplWallet counterpartyWallet,
            string brokerOwner)
        {
            string txType = preparedTx["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"TransactionType must be LoanSet, got: {txType}");

            string account = preparedTx["Account"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(account))
                throw new ValidationException("LoanSet must include Account before signing.");

            (XrplWallet submitter, XrplWallet counterpartySigner) = ResolveRoles(account, submitterWallet, counterpartyWallet);

            string counterpartyField = preparedTx["Counterparty"]?.GetValue<string>();
            string counterparty = string.IsNullOrWhiteSpace(counterpartyField) ? brokerOwner : counterpartyField;

            if (!string.IsNullOrWhiteSpace(counterparty) &&
                !string.Equals(counterparty, counterpartySigner.ClassicAddress, StringComparison.Ordinal))
            {
                string source = string.IsNullOrWhiteSpace(counterpartyField) ? "broker owner" : "Counterparty field";
                throw new ValidationException($"The counterparty ({source}: {counterparty}) does not match the co-signing wallet ({counterpartySigner.ClassicAddress}).");
            }

            if (!string.IsNullOrWhiteSpace(brokerOwner) &&
                !string.Equals(account, brokerOwner, StringComparison.Ordinal) &&
                !string.Equals(counterparty, brokerOwner, StringComparison.Ordinal))
            {
                throw new ValidationException($"Neither Account ({account}) nor the counterparty ({counterparty}) is the broker owner ({brokerOwner}).");
            }

            return CoSigningEngine.SignBoth(preparedTx, submitter, counterpartySigner, "CounterpartySignature");
        }

        /// <summary>
        /// Orders the two wallets by role: the one whose address is <c>Account</c> submits.
        /// </summary>
        private static (XrplWallet Submitter, XrplWallet Counterparty) ResolveRoles(
            string account,
            XrplWallet first,
            XrplWallet second)
        {
            if (first is null || second is null)
                throw new ValidationException("Both the submitter's and the counterparty's wallets are required.");

            if (string.Equals(first.ClassicAddress, account, StringComparison.Ordinal))
                return (first, second);
            if (string.Equals(second.ClassicAddress, account, StringComparison.Ordinal))
                return (second, first);

            throw new ValidationException($"Neither wallet ({first.ClassicAddress}, {second.ClassicAddress}) is the LoanSet Account ({account}).");
        }

        /// <summary>
        /// V2 — Combine independently signed submitter and counterparty blobs.
        /// The submitter blob has TxnSignature but no CounterpartySignature.
        /// The counterparty blob has CounterpartySignature but no TxnSignature.
        /// </summary>
        /// <param name="submitterSignedBlob">Hex blob signed by the submitting account (has TxnSignature).</param>
        /// <param name="counterpartySignedBlob">Hex blob signed by the counterparty (has CounterpartySignature).</param>
        /// <returns>Combined fully signed blob.</returns>
        public static SignatureResult CombineLoanSignatures(
            string submitterSignedBlob,
            string counterpartySignedBlob)
        {
            RequireLoanSet(submitterSignedBlob, "Submitter");
            RequireLoanSet(counterpartySignedBlob, "Counterparty");
            return CoSigningEngine.Combine(submitterSignedBlob, counterpartySignedBlob, "CounterpartySignature", "Counterparty");
        }

        /// <summary>
        /// Combines a submitter part with the portable Signer entries of a multisig
        /// counterparty: entries from <paramref name="counterpartySignerAccounts"/> land in
        /// <c>CounterpartySignature.Signers</c>, any other entry in <c>tx.Signers</c>
        /// (a multisig submitter). Offline; for ledger-driven routing use
        /// <c>IXrplClient.ComposeSignatures</c>.
        /// </summary>
        /// <param name="partBlobs">The submitter's blob and the counterparty-side multisign parts.</param>
        /// <param name="counterpartySignerAccounts">Accounts of the counterparty's SignerList.</param>
        public static SignatureResult CombineLoanSignatures(
            IEnumerable<string> partBlobs,
            IReadOnlyCollection<string> counterpartySignerAccounts)
        {
            List<string> parts = partBlobs?.ToList()
                ?? throw new ValidationException("At least one partially signed blob is required.");
            foreach (string blob in parts)
                RequireLoanSet(blob, "Part");
            return SignatureComposer.ComposeSignatures(parts, null, counterpartySignerAccounts);
        }

        /// <summary>
        /// V3 — The submitter signs a partially signed LoanSet blob (one that already has CounterpartySignature).
        /// Decodes the blob, strips CounterpartySignature to compute the correct preimage,
        /// adds the submitter's TxnSignature, then restores CounterpartySignature for encoding.
        /// </summary>
        /// <param name="partiallySignedBlob">Hex blob from the counterparty's SignAsLoanCounterparty (has CounterpartySignature, no TxnSignature).</param>
        /// <param name="submitterWallet">The submitting account's wallet (<c>Account</c>): the broker owner or the borrower.</param>
        /// <returns>Fully signed transaction blob and hash.</returns>
        public static SignatureResult SubmitterSign(string partiallySignedBlob, XrplWallet submitterWallet)
        {
            RequireLoanSet(partiallySignedBlob, "Partially signed");
            return CoSigningEngine.FinalizeAsSubmitter(partiallySignedBlob, submitterWallet, "CounterpartySignature");
        }

        /// <summary>
        /// V3 — The submitter signs a partially signed LoanSet blob. Same as
        /// <see cref="SubmitterSign"/>: the submitter need not be the broker.
        /// </summary>
        /// <param name="partiallySignedBlob">Hex blob from the counterparty's SignAsLoanCounterparty (has CounterpartySignature, no TxnSignature).</param>
        /// <param name="brokerWallet">The submitting account's wallet (<c>Account</c>).</param>
        /// <returns>Fully signed transaction blob and hash.</returns>
        [Obsolete("Use SubmitterSign: the account that submits a LoanSet may be the borrower as well as the broker owner.")]
        public static SignatureResult BrokerSign(string partiallySignedBlob, XrplWallet brokerWallet)
            => SubmitterSign(partiallySignedBlob, brokerWallet);

        private static void RequireLoanSet(string blob, string label)
        {
            JsonObject tx = XrplBinaryCodec.Decode(blob).AsObject();
            string txType = tx["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "LoanSet", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"{label} blob TransactionType must be LoanSet, got: {txType}");
        }

        /// <summary>
        /// Computes the bytes the counterparty signs into CounterpartySignature: the transaction
        /// under the counterparty prefix, which since fixCleanup3_4_0 is not what the submitter signs.
        /// </summary>
        internal static byte[] GetCounterpartyPreimage(JsonObject txJson)
            => CoSigningEngine.GetSigningPreimage(txJson, CoSigningEngine.PrefixFor("CounterpartySignature"));

    }
}
