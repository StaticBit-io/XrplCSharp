using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    // ConfidentialTransfer amendment (XLS confidential MPT transfers).
    // Encrypted amounts, commitments and proofs are hex-encoded blobs;
    // the SDK treats them as opaque strings produced by an external prover.

    /// <summary>
    /// Converts a public MPT balance into a confidential (encrypted) balance.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public interface IConfidentialMPTConvert : ITransactionCommon
    {
        /// <summary>
        /// The ID of the MPT issuance whose balance is being converted.
        /// </summary>
        string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// The public amount being converted, as a decimal string.
        /// </summary>
        string MPTAmount { get; set; }

        /// <summary>
        /// (Optional) The holder's public encryption key, hex-encoded - the key the holder's
        /// confidential balances are encrypted under.
        /// </summary>
        string HolderEncryptionKey { get; set; }

        /// <summary>
        /// The converted amount encrypted under the holder's key, hex-encoded.
        /// </summary>
        string HolderEncryptedAmount { get; set; }

        /// <summary>
        /// The converted amount encrypted under the issuer's key, hex-encoded.
        /// </summary>
        string IssuerEncryptedAmount { get; set; }

        /// <summary>
        /// (Optional) The converted amount encrypted under the auditor's key, hex-encoded.
        /// </summary>
        string AuditorEncryptedAmount { get; set; }

        /// <summary>
        /// The blinding factor of the amount commitment, hex-encoded.
        /// </summary>
        string BlindingFactor { get; set; }

        /// <summary>
        /// (Optional) The zero-knowledge proof over the encrypted amounts, hex-encoded.
        /// </summary>
        string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTConvert" />
    public class ConfidentialMPTConvert : TransactionRequest, IConfidentialMPTConvert
    {
        public ConfidentialMPTConvert()
        {
            TransactionType = TransactionType.ConfidentialMPTConvert;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptionKey")]
        public string HolderEncryptionKey { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTConvert" />
    public class ConfidentialMPTConvertResponse : TransactionResponse, IConfidentialMPTConvert
    {
        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptionKey")]
        public string HolderEncryptionKey { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <summary>
    /// Merges the confidential inbox balance into the confidential spending balance.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public interface IConfidentialMPTMergeInbox : ITransactionCommon
    {
        /// <summary>
        /// The ID of the MPT issuance whose inbox balance is merged into the spending balance.
        /// </summary>
        string MPTokenIssuanceID { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTMergeInbox" />
    public class ConfidentialMPTMergeInbox : TransactionRequest, IConfidentialMPTMergeInbox
    {
        public ConfidentialMPTMergeInbox()
        {
            TransactionType = TransactionType.ConfidentialMPTMergeInbox;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTMergeInbox" />
    public class ConfidentialMPTMergeInboxResponse : TransactionResponse, IConfidentialMPTMergeInbox
    {
        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }
    }

    /// <summary>
    /// Converts a confidential (encrypted) MPT balance back into a public balance.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public interface IConfidentialMPTConvertBack : ITransactionCommon
    {
        /// <summary>
        /// The ID of the MPT issuance whose balance is being converted back.
        /// </summary>
        string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// The public amount being converted back, as a decimal string.
        /// </summary>
        string MPTAmount { get; set; }

        /// <summary>
        /// The converted amount encrypted under the holder's key, hex-encoded.
        /// </summary>
        string HolderEncryptedAmount { get; set; }

        /// <summary>
        /// The converted amount encrypted under the issuer's key, hex-encoded.
        /// </summary>
        string IssuerEncryptedAmount { get; set; }

        /// <summary>
        /// (Optional) The converted amount encrypted under the auditor's key, hex-encoded.
        /// </summary>
        string AuditorEncryptedAmount { get; set; }

        /// <summary>
        /// The blinding factor of the amount commitment, hex-encoded.
        /// </summary>
        string BlindingFactor { get; set; }

        /// <summary>
        /// The zero-knowledge proof over the encrypted amounts, hex-encoded.
        /// </summary>
        string ZKProof { get; set; }

        /// <summary>
        /// A commitment to the confidential balance left after the conversion, hex-encoded.
        /// </summary>
        string BalanceCommitment { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTConvertBack" />
    public class ConfidentialMPTConvertBack : TransactionRequest, IConfidentialMPTConvertBack
    {
        public ConfidentialMPTConvertBack()
        {
            TransactionType = TransactionType.ConfidentialMPTConvertBack;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTConvertBack" />
    public class ConfidentialMPTConvertBackResponse : TransactionResponse, IConfidentialMPTConvertBack
    {
        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }
    }

    /// <summary>
    /// Sends a confidential MPT amount to another holder's confidential inbox.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public interface IConfidentialMPTSend : ITransactionCommon, IDestination
    {
        /// <summary>
        /// The ID of the MPT issuance the amount is sent in.
        /// </summary>
        string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// The account whose confidential inbox receives the amount.
        /// </summary>
        string Destination { get; set; }

        /// <summary>
        /// (Optional) Arbitrary tag that identifies the reason for the transfer to the destination,
        /// or a hosted recipient.
        /// </summary>
        uint? DestinationTag { get; set; }

        /// <summary>
        /// The sent amount encrypted under the sender's key, hex-encoded.
        /// </summary>
        string SenderEncryptedAmount { get; set; }

        /// <summary>
        /// The sent amount encrypted under the destination's key, hex-encoded.
        /// </summary>
        string DestinationEncryptedAmount { get; set; }

        /// <summary>
        /// The sent amount encrypted under the issuer's key, hex-encoded.
        /// </summary>
        string IssuerEncryptedAmount { get; set; }

        /// <summary>
        /// (Optional) The sent amount encrypted under the auditor's key, hex-encoded.
        /// </summary>
        string AuditorEncryptedAmount { get; set; }

        /// <summary>
        /// The zero-knowledge proof over the encrypted amounts and commitments, hex-encoded.
        /// </summary>
        string ZKProof { get; set; }

        /// <summary>
        /// A commitment to the sent amount, hex-encoded.
        /// </summary>
        string AmountCommitment { get; set; }

        /// <summary>
        /// A commitment to the sender's confidential balance left after the transfer, hex-encoded.
        /// </summary>
        string BalanceCommitment { get; set; }

        /// <summary>
        /// (Optional) Set of Credentials (object IDs, hex 64-char each) to authorize the transfer
        /// when the destination account requires Deposit Authorization with credential-based
        /// preauth (XLS-70).
        /// </summary>
        List<string> CredentialIDs { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTSend" />
    public class ConfidentialMPTSend : TransactionRequest, IConfidentialMPTSend, IDestination
    {
        public ConfidentialMPTSend()
        {
            TransactionType = TransactionType.ConfidentialMPTSend;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DestinationTag")]
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SenderEncryptedAmount")]
        public string SenderEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DestinationEncryptedAmount")]
        public string DestinationEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AmountCommitment")]
        public string AmountCommitment { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CredentialIDs")]
        public List<string> CredentialIDs { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTSend" />
    public class ConfidentialMPTSendResponse : TransactionResponse, IConfidentialMPTSend, IDestination
    {
        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DestinationTag")]
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SenderEncryptedAmount")]
        public string SenderEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DestinationEncryptedAmount")]
        public string DestinationEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AmountCommitment")]
        public string AmountCommitment { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CredentialIDs")]
        public List<string> CredentialIDs { get; set; }
    }

    /// <summary>
    /// Claws back a confidential MPT amount from a holder (issuer only).
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public interface IConfidentialMPTClawback : ITransactionCommon
    {
        /// <summary>
        /// The ID of the MPT issuance the amount is clawed back in.
        /// </summary>
        string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// The account the amount is clawed back from.
        /// </summary>
        string Holder { get; set; }

        /// <summary>
        /// The amount being clawed back, as a decimal string.
        /// </summary>
        string MPTAmount { get; set; }

        /// <summary>
        /// The zero-knowledge proof over the clawed-back amount, hex-encoded.
        /// </summary>
        string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTClawback" />
    public class ConfidentialMPTClawback : TransactionRequest, IConfidentialMPTClawback
    {
        public ConfidentialMPTClawback()
        {
            TransactionType = TransactionType.ConfidentialMPTClawback;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <inheritdoc cref="IConfidentialMPTClawback" />
    public class ConfidentialMPTClawbackResponse : TransactionResponse, IConfidentialMPTClawback
    {
        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    public partial class Validation
    {
        private static void ValidateConfidentialCommon(Dictionary<string, object> tx, string txName)
        {
            if (!tx.TryGetValue("MPTokenIssuanceID", out var issuance) || issuance is not string)
                throw new ValidationException($"{txName}: missing field MPTokenIssuanceID");
            return;
        }

        public static void ValidateConfidentialMPTConvert(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTConvert");
        }

        public static void ValidateConfidentialMPTMergeInbox(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTMergeInbox");
        }

        public static void ValidateConfidentialMPTConvertBack(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTConvertBack");
        }

        public static void ValidateConfidentialMPTSend(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTSend");
            if (!tx.TryGetValue("Destination", out var dest) || dest is not string)
                throw new ValidationException("ConfidentialMPTSend: missing field Destination");
        }

        public static void ValidateConfidentialMPTClawback(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);
            ValidateConfidentialCommon(tx, "ConfidentialMPTClawback");
            if (!tx.TryGetValue("Holder", out var holder) || holder is not string)
                throw new ValidationException("ConfidentialMPTClawback: missing field Holder");
        }
    }
}
