using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    // ConfidentialMPTKeyRotation amendment: rotating the ElGamal keys a confidential MPT
    // balance is encrypted under. As in ConfidentialMPT.cs, ciphertexts, keys and proofs are
    // hex-encoded blobs produced by an external prover; the SDK does not check their contents.

    /// <summary>
    /// Re-encrypts the issuer or auditor mirror of a confidential MPT balance under the
    /// issuance's current key.
    /// </summary>
    /// <remarks>
    /// With <see cref="Holder"/> present the issuer submits it for that holder; without it the
    /// holder submits it for its own balance.
    /// Requires the ConfidentialMPTKeyRotation amendment. This feature is in draft and subject to change.
    /// </remarks>
    public interface IConfidentialMPTMirrorUpdate : ITransactionCommon
    {
        /// <summary>
        /// The ID of the MPT issuance whose mirror is updated.
        /// </summary>
        string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// (Optional) The holder whose mirror the issuer updates. Absent when the holder
        /// updates its own mirror.
        /// </summary>
        string Holder { get; set; }

        /// <summary>
        /// (Optional) The balance re-encrypted under the issuer's key, hex-encoded.
        /// At least one of this and <see cref="AuditorEncryptedAmount"/> is required.
        /// </summary>
        string IssuerEncryptedAmount { get; set; }

        /// <summary>
        /// (Optional) The balance re-encrypted under the auditor's key, hex-encoded.
        /// </summary>
        string AuditorEncryptedAmount { get; set; }

        /// <summary>
        /// The equality proof that the re-encrypted ciphertexts hold the same balance, hex-encoded.
        /// </summary>
        string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTMirrorUpdate" />
    public class ConfidentialMPTMirrorUpdate : TransactionRequest, IConfidentialMPTMirrorUpdate
    {
        public ConfidentialMPTMirrorUpdate()
        {
            TransactionType = TransactionType.ConfidentialMPTMirrorUpdate;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTMirrorUpdate" />
    public class ConfidentialMPTMirrorUpdateResponse : TransactionResponse, IConfidentialMPTMirrorUpdate
    {
        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <summary>
    /// Mode flags of <see cref="ConfidentialMPTHolderKeyUpdate"/> (rippled TxFlags.h). Exactly one is set.
    /// </summary>
    [Flags]
    public enum ConfidentialMPTHolderKeyUpdateFlags : uint
    {
        /// <summary>
        /// Replace the holder's encryption key; the balances are re-encrypted under the new key.
        /// </summary>
        tfHolderKeyRotation = 0x00010000,

        /// <summary>
        /// Install a new key for a holder that lost the old one; no balances are supplied,
        /// since the holder cannot decrypt the current ones.
        /// </summary>
        tfHolderKeyRecovery = 0x00020000,

        /// <summary>
        /// Revoke a pending recovery authorization; carries no key, balances or proof.
        /// </summary>
        tfCancelRecovery = 0x00040000,
    }

    /// <summary>
    /// Rotates or recovers a confidential MPT holder's ElGamal encryption key, or cancels a recovery.
    /// </summary>
    /// <remarks>
    /// Not delegable. Requires the ConfidentialMPTKeyRotation amendment. This feature is in draft and subject to change.
    /// </remarks>
    public interface IConfidentialMPTHolderKeyUpdate : ITransactionCommon
    {
        /// <summary>
        /// The mode of the update (see <see cref="ConfidentialMPTHolderKeyUpdateFlags"/>).
        /// </summary>
        new ConfidentialMPTHolderKeyUpdateFlags? Flags { get; set; }

        /// <summary>
        /// The ID of the MPT issuance the holder's balance is in.
        /// </summary>
        string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// (Optional) The holder's new encryption key, a compressed EC point, hex-encoded.
        /// Required for rotation and recovery, absent when cancelling.
        /// </summary>
        string HolderEncryptionKey { get; set; }

        /// <summary>
        /// (Optional) The spending balance re-encrypted under the new key, hex-encoded.
        /// Required for rotation only.
        /// </summary>
        string ConfidentialBalanceSpending { get; set; }

        /// <summary>
        /// (Optional) The inbox balance re-encrypted under the new key, hex-encoded.
        /// Required for rotation only.
        /// </summary>
        string ConfidentialBalanceInbox { get; set; }

        /// <summary>
        /// (Optional) The proof for the new key and balances, hex-encoded.
        /// Required for rotation and recovery, absent when cancelling.
        /// </summary>
        string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTHolderKeyUpdate" />
    public class ConfidentialMPTHolderKeyUpdate : TransactionRequest, IConfidentialMPTHolderKeyUpdate
    {
        public ConfidentialMPTHolderKeyUpdate()
        {
            TransactionType = TransactionType.ConfidentialMPTHolderKeyUpdate;
        }

        /// <summary>
        /// Typed view over the base Flags value (see <see cref="ConfidentialMPTHolderKeyUpdateFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new ConfidentialMPTHolderKeyUpdateFlags? Flags
        {
            get => base.Flags.HasValue ? (ConfidentialMPTHolderKeyUpdateFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptionKey")]
        public string HolderEncryptionKey { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ConfidentialBalanceSpending")]
        public string ConfidentialBalanceSpending { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ConfidentialBalanceInbox")]
        public string ConfidentialBalanceInbox { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTHolderKeyUpdate" />
    public class ConfidentialMPTHolderKeyUpdateResponse : TransactionResponse, IConfidentialMPTHolderKeyUpdate
    {
        /// <summary>
        /// Typed view over the base Flags value (see <see cref="ConfidentialMPTHolderKeyUpdateFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new ConfidentialMPTHolderKeyUpdateFlags? Flags
        {
            get => base.Flags.HasValue ? (ConfidentialMPTHolderKeyUpdateFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptionKey")]
        public string HolderEncryptionKey { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ConfidentialBalanceSpending")]
        public string ConfidentialBalanceSpending { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ConfidentialBalanceInbox")]
        public string ConfidentialBalanceInbox { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    public partial class Validation
    {
        public static void ValidateConfidentialMPTMirrorUpdate(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTMirrorUpdate");

            // Mirror of rippled ConfidentialMPTMirrorUpdate::preflight: at least one ciphertext
            // is updated, and the proof is required in both modes.
            if (!tx.ContainsKey("IssuerEncryptedAmount") && !tx.ContainsKey("AuditorEncryptedAmount"))
                throw new ValidationException("ConfidentialMPTMirrorUpdate: IssuerEncryptedAmount or AuditorEncryptedAmount is required");
            if (!tx.TryGetValue("ZKProof", out var proof) || proof is not string)
                throw new ValidationException("ConfidentialMPTMirrorUpdate: missing field ZKProof");
        }

        public static void ValidateConfidentialMPTHolderKeyUpdate(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTHolderKeyUpdate");

            // Mirror of rippled ConfidentialMPTHolderKeyUpdate::preflight.
            uint flags = ExtractFlags(tx);
            const uint modeFlags = (uint)(ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRotation
                | ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRecovery
                | ConfidentialMPTHolderKeyUpdateFlags.tfCancelRecovery);
            if (System.Numerics.BitOperations.PopCount(flags & modeFlags) != 1)
                throw new ValidationException("ConfidentialMPTHolderKeyUpdate: exactly one of tfHolderKeyRotation, tfHolderKeyRecovery or tfCancelRecovery must be set");

            bool hasHolderKey = tx.ContainsKey("HolderEncryptionKey");
            bool hasSpending = tx.ContainsKey("ConfidentialBalanceSpending");
            bool hasInbox = tx.ContainsKey("ConfidentialBalanceInbox");
            bool hasProof = tx.ContainsKey("ZKProof");

            if ((flags & (uint)ConfidentialMPTHolderKeyUpdateFlags.tfCancelRecovery) != 0)
            {
                if (hasHolderKey || hasSpending || hasInbox || hasProof)
                    throw new ValidationException("ConfidentialMPTHolderKeyUpdate: tfCancelRecovery carries no HolderEncryptionKey, balances or ZKProof");
                return;
            }

            if (!hasHolderKey || !hasProof)
                throw new ValidationException("ConfidentialMPTHolderKeyUpdate: HolderEncryptionKey and ZKProof are required for rotation and recovery");

            bool rotation = (flags & (uint)ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRotation) != 0;
            if (rotation && (!hasSpending || !hasInbox))
                throw new ValidationException("ConfidentialMPTHolderKeyUpdate: tfHolderKeyRotation requires ConfidentialBalanceSpending and ConfidentialBalanceInbox");
            if (!rotation && (hasSpending || hasInbox))
                throw new ValidationException("ConfidentialMPTHolderKeyUpdate: tfHolderKeyRecovery must not carry ConfidentialBalanceSpending or ConfidentialBalanceInbox");
        }
    }
}
