#nullable enable
using System;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Keypairs;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;

namespace Xrpl.Wallet
{
    /// <summary>
    /// Shared engine behind the inner co-signature helpers. SponsorSignature
    /// (XLS-68) and CounterpartySignature (XLS-66) follow one protocol shape —
    /// an inner not-signing STObject signed over the same preimage as the main
    /// signature — so the V1/V2/V3 flows differ only by the field name and the
    /// wording of their errors. SponsorSigningHelper and LoanSigningHelper are
    /// thin facades over this class.
    /// </summary>
    internal static class CoSigningEngine
    {
        /// <summary>
        /// Computes the signing preimage bytes. The submitter and every
        /// co-signer sign these same bytes (inner signature objects are
        /// kNotSigning and never enter the preimage).
        /// </summary>
        internal static byte[] GetSigningPreimage(JsonObject txJson)
        {
            string signingHex = XrplBinaryCodec.EncodeForSigning(txJson);
            return AddressCodec.Utils.FromHexToBytes(signingHex);
        }

        /// <summary>Removes the signature-bearing fields for body comparison.</summary>
        internal static JsonObject Canonicalize(JsonObject tx, string coSignatureField) =>
            tx.WithoutFields("TxnSignature", "SigningPubKey", coSignatureField);

        /// <summary>
        /// V1 — both keys local: the co-signer and the submitter sign the same
        /// preimage; the co-signature lands in <paramref name="coSignatureField"/>.
        /// </summary>
        internal static SignatureResult SignBoth(
            JsonObject preparedTx,
            XrplWallet submitterWallet,
            XrplWallet coSignerWallet,
            string coSignatureField)
        {
            JsonObject tx = preparedTx.DeepClone().AsObject();
            tx["SigningPubKey"] = submitterWallet.PublicKey;
            tx.Remove(coSignatureField);
            tx.Remove("TxnSignature");

            byte[] signingBytes = GetSigningPreimage(tx);

            string coSignature = XrplKeypairs.Sign(signingBytes, coSignerWallet.PrivateKey);
            tx[coSignatureField] = SignatureObject.Single(coSignerWallet.PublicKey, coSignature).ToJsonObject();

            tx["TxnSignature"] = XrplKeypairs.Sign(signingBytes, submitterWallet.PrivateKey);

            return Encode(tx);
        }

        /// <summary>
        /// V2 — merges two independently signed blobs of the same transaction:
        /// the submitter's (TxnSignature) and the co-signer's
        /// (<paramref name="coSignatureField"/>).
        /// </summary>
        internal static SignatureResult Combine(
            string submitterSignedBlob,
            string coSignerSignedBlob,
            string coSignatureField,
            string coSignerLabel)
        {
            JsonObject submitterTx = XrplBinaryCodec.Decode(submitterSignedBlob).AsObject();
            JsonObject coSignerTx = XrplBinaryCodec.Decode(coSignerSignedBlob).AsObject();

            string? submitterPubKey = submitterTx["SigningPubKey"]?.GetValue<string>();
            string? coSignerSidePubKey = coSignerTx["SigningPubKey"]?.GetValue<string>();
            if (!string.Equals(submitterPubKey, coSignerSidePubKey, StringComparison.Ordinal))
                throw new ValidationException("Incompatible SigningPubKey values. Both blobs must use the submitter's SigningPubKey.");

            if (!JsonNode.DeepEquals(Canonicalize(submitterTx, coSignatureField), Canonicalize(coSignerTx, coSignatureField)))
                throw new ValidationException("Incompatible transaction bodies. Both inputs must have identical non-signing fields.");

            JsonObject combined = submitterTx.DeepClone().AsObject();

            JsonNode coSignature = coSignerTx[coSignatureField]
                ?? throw new ValidationException($"{coSignerLabel} blob is missing {coSignatureField}.");
            combined[coSignatureField] = coSignature.DeepClone();

            if (combined["TxnSignature"] == null)
                throw new ValidationException("Submitter blob is missing TxnSignature.");

            return Encode(combined);
        }

        /// <summary>
        /// V3 — the submitter finalizes a partially signed blob that already
        /// carries the co-signature in <paramref name="coSignatureField"/>.
        /// </summary>
        internal static SignatureResult FinalizeAsSubmitter(
            string partiallySignedBlob,
            XrplWallet submitterWallet,
            string coSignatureField)
        {
            JsonObject tx = XrplBinaryCodec.Decode(partiallySignedBlob).AsObject();

            JsonNode coSignature = tx[coSignatureField]?.DeepClone()
                ?? throw new ValidationException($"Partially signed blob is missing {coSignatureField}.");

            tx.Remove(coSignatureField);
            tx.Remove("TxnSignature");

            string? existingSigningPubKey = tx["SigningPubKey"]?.GetValue<string>();
            if (string.IsNullOrEmpty(existingSigningPubKey))
            {
                throw new ValidationException($"The {coSignatureField} was made over a multisig submitter form (empty SigningPubKey); a single main signature would invalidate it. Compose multisig parts instead.");
            }
            if (!string.Equals(existingSigningPubKey, submitterWallet.PublicKey, StringComparison.Ordinal))
            {
                throw new ValidationException("Partially signed blob SigningPubKey does not match submitter wallet.");
            }
            tx["SigningPubKey"] = submitterWallet.PublicKey;

            byte[] signingBytes = GetSigningPreimage(tx);
            tx["TxnSignature"] = XrplKeypairs.Sign(signingBytes, submitterWallet.PrivateKey);
            tx[coSignatureField] = coSignature;

            return Encode(tx);
        }

        private static SignatureResult Encode(JsonObject tx)
        {
            string txBlob = XrplBinaryCodec.Encode(tx);
            return new SignatureResult(txBlob, HashLedger.HashSignedTx(txBlob));
        }
    }
}
