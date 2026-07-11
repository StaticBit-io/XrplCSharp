using System;
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
    /// Helper for sponsored transaction signing (XLS-68).
    /// A sponsored transaction carries the common fields Sponsor and SponsorFlags
    /// (spfSponsorFee = 1, spfSponsorReserve = 2) and, when the sponsorship requires it,
    /// the sponsor's co-signature: SponsorSignature (inner STObject with
    /// SigningPubKey + TxnSignature over the same preimage as the main signature).
    ///
    /// Signing patterns (analogous to LoanSet broker/counterparty):
    ///
    /// <b>V1 — Automatic (both keys available):</b>
    /// <code>
    /// var result = SponsorSigningHelper.SignSponsored(preparedTx, submitterWallet, sponsorWallet);
    /// await client.SubmitRequest(result.TxBlob);
    /// </code>
    ///
    /// <b>V2 — Parallel (keys on separate devices):</b>
    /// <code>
    /// var sponsorSig = sponsorWallet.SignAsSponsor(preparedTx);
    /// var submitterSig = submitterWallet.Sign(preparedTx);
    /// var combined = SponsorSigningHelper.CombineSponsorSignatures(submitterSig.TxBlob, sponsorSig.TxBlob);
    /// </code>
    ///
    /// <b>V3 — Sequential (sponsor signs first, passes to submitter):</b>
    /// <code>
    /// var withSponsor = sponsorWallet.SignAsSponsor(preparedTx);
    /// var final = SponsorSigningHelper.SubmitterSign(withSponsor.TxBlob, submitterWallet);
    /// </code>
    /// </summary>
    public static class SponsorSigningHelper
    {
        /// <summary>
        /// Prepares a transaction JSON for sponsored signing: verifies the Sponsor
        /// common field, sets the submitter's SigningPubKey and removes signature fields.
        /// </summary>
        /// <param name="transaction">Any transaction carrying Sponsor/SponsorFlags (autofilled).</param>
        /// <param name="submitterWallet">The submitting account's wallet (fee-payer is the sponsor per SponsorFlags).</param>
        public static JsonObject PrepareForSigning(ITransactionRequest transaction, XrplWallet submitterWallet)
        {
            string txJsonStr = JsonSerializer.Serialize(transaction, transaction.GetType(), XrplJsonOptions.Default);
            JsonObject txJson = JsonNode.Parse(txJsonStr)?.AsObject()
                ?? throw new ValidationException("Failed to serialize transaction to JSON");

            if (txJson["Sponsor"] == null)
                throw new ValidationException("Sponsored transaction must carry the Sponsor field.");

            txJson["SigningPubKey"] = submitterWallet.PublicKey;
            txJson.Remove("SponsorSignature");
            txJson.Remove("TxnSignature");
            return txJson;
        }

        /// <summary>
        /// V1 — Automatic signing: both the submitter and the sponsor sign locally.
        /// </summary>
        public static SignatureResult SignSponsored(
            JsonObject preparedTx,
            XrplWallet submitterWallet,
            XrplWallet sponsorWallet)
        {
            JsonObject tx = preparedTx.DeepClone().AsObject();
            VerifySponsorMatches(tx, sponsorWallet);

            tx["SigningPubKey"] = submitterWallet.PublicKey;
            tx.Remove("SponsorSignature");
            tx.Remove("TxnSignature");

            byte[] signingBytes = GetSigningPreimage(tx);

            string sponsorSig = XrplKeypairs.Sign(signingBytes, sponsorWallet.PrivateKey);
            tx["SponsorSignature"] = new JsonObject
            {
                ["SigningPubKey"] = sponsorWallet.PublicKey,
                ["TxnSignature"] = sponsorSig,
            };

            string submitterSig = XrplKeypairs.Sign(signingBytes, submitterWallet.PrivateKey);
            tx["TxnSignature"] = submitterSig;

            string txBlob = XrplBinaryCodec.Encode(tx);
            return new SignatureResult(txBlob, HashLedger.HashSignedTx(txBlob));
        }

        /// <summary>
        /// V2 — Combine independently signed submitter and sponsor blobs.
        /// The submitter blob has TxnSignature but no SponsorSignature;
        /// the sponsor blob has SponsorSignature but no TxnSignature.
        /// </summary>
        public static SignatureResult CombineSponsorSignatures(
            string submitterSignedBlob,
            string sponsorSignedBlob)
        {
            JsonObject submitterTx = XrplBinaryCodec.Decode(submitterSignedBlob).AsObject();
            JsonObject sponsorTx = XrplBinaryCodec.Decode(sponsorSignedBlob).AsObject();

            string submitterPubKey = submitterTx["SigningPubKey"]?.GetValue<string>();
            string sponsorSidePubKey = sponsorTx["SigningPubKey"]?.GetValue<string>();
            if (!string.Equals(submitterPubKey, sponsorSidePubKey, StringComparison.Ordinal))
                throw new ValidationException("Incompatible SigningPubKey values. Both blobs must use the submitter's SigningPubKey.");

            if (!JsonNode.DeepEquals(Canonicalize(submitterTx), Canonicalize(sponsorTx)))
                throw new ValidationException("Incompatible sponsored transaction bodies. Both inputs must have identical non-signing fields.");

            JsonObject combined = submitterTx.DeepClone().AsObject();

            JsonNode sponsorSig = sponsorTx["SponsorSignature"]
                ?? throw new ValidationException("Sponsor blob is missing SponsorSignature.");
            combined["SponsorSignature"] = sponsorSig.DeepClone();

            if (combined["TxnSignature"] == null)
                throw new ValidationException("Submitter blob is missing TxnSignature.");

            string txBlob = XrplBinaryCodec.Encode(combined);
            return new SignatureResult(txBlob, HashLedger.HashSignedTx(txBlob));
        }

        /// <summary>
        /// V3 — The submitter signs a partially signed blob that already has SponsorSignature.
        /// </summary>
        public static SignatureResult SubmitterSign(string partiallySignedBlob, XrplWallet submitterWallet)
        {
            JsonObject tx = XrplBinaryCodec.Decode(partiallySignedBlob).AsObject();

            JsonNode sponsorSig = tx["SponsorSignature"]?.DeepClone()
                ?? throw new ValidationException("Partially signed blob is missing SponsorSignature.");

            tx.Remove("SponsorSignature");
            tx.Remove("TxnSignature");

            string existingSigningPubKey = tx["SigningPubKey"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(existingSigningPubKey) &&
                !string.Equals(existingSigningPubKey, submitterWallet.PublicKey, StringComparison.Ordinal))
            {
                throw new ValidationException("Partially signed blob SigningPubKey does not match submitter wallet.");
            }
            tx["SigningPubKey"] = submitterWallet.PublicKey;

            byte[] signingBytes = GetSigningPreimage(tx);
            string submitterSig = XrplKeypairs.Sign(signingBytes, submitterWallet.PrivateKey);

            tx["TxnSignature"] = submitterSig;
            tx["SponsorSignature"] = sponsorSig;

            string txBlob = XrplBinaryCodec.Encode(tx);
            return new SignatureResult(txBlob, HashLedger.HashSignedTx(txBlob));
        }

        /// <summary>
        /// Computes the signing preimage bytes for a sponsored transaction.
        /// Both the submitter and the sponsor sign the same preimage.
        /// </summary>
        public static byte[] GetSigningPreimage(JsonObject txJson)
        {
            string signingHex = XrplBinaryCodec.EncodeForSigning(txJson);
            return AddressCodec.Utils.FromHexToBytes(signingHex);
        }

        internal static void VerifySponsorMatches(JsonObject tx, XrplWallet sponsorWallet)
        {
            string sponsor = tx["Sponsor"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(sponsor))
                throw new ValidationException("Sponsored transaction must carry the Sponsor field.");
            if (!string.Equals(sponsor, sponsorWallet.ClassicAddress, StringComparison.Ordinal))
                throw new ValidationException($"Sponsor field ({sponsor}) does not match the sponsor wallet ({sponsorWallet.ClassicAddress}).");
        }

        private static JsonObject Canonicalize(JsonObject tx)
        {
            JsonObject canon = tx.DeepClone().AsObject();
            canon.Remove("TxnSignature");
            canon.Remove("SigningPubKey");
            canon.Remove("SponsorSignature");
            return canon;
        }
    }
}
