using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
    public class ConfidentialMPTConvert : TransactionRequest
    {
        public ConfidentialMPTConvert()
        {
            TransactionType = TransactionType.ConfidentialMPTConvert;
        }

        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        /// <summary>The public amount being converted, as a decimal string.</summary>
        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        [JsonPropertyName("HolderEncryptionKey")]
        public string HolderEncryptionKey { get; set; }

        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <inheritdoc cref="ConfidentialMPTConvert" />
    public class ConfidentialMPTConvertResponse : TransactionResponse
    {
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        [JsonPropertyName("HolderEncryptionKey")]
        public string HolderEncryptionKey { get; set; }

        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <summary>
    /// Merges the confidential inbox balance into the confidential spending balance.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public class ConfidentialMPTMergeInbox : TransactionRequest
    {
        public ConfidentialMPTMergeInbox()
        {
            TransactionType = TransactionType.ConfidentialMPTMergeInbox;
        }

        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }
    }

    /// <inheritdoc cref="ConfidentialMPTMergeInbox" />
    public class ConfidentialMPTMergeInboxResponse : TransactionResponse
    {
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }
    }

    /// <summary>
    /// Converts a confidential (encrypted) MPT balance back into a public balance.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public class ConfidentialMPTConvertBack : TransactionRequest
    {
        public ConfidentialMPTConvertBack()
        {
            TransactionType = TransactionType.ConfidentialMPTConvertBack;
        }

        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }
    }

    /// <inheritdoc cref="ConfidentialMPTConvertBack" />
    public class ConfidentialMPTConvertBackResponse : TransactionResponse
    {
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        [JsonPropertyName("HolderEncryptedAmount")]
        public string HolderEncryptedAmount { get; set; }

        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        [JsonPropertyName("BlindingFactor")]
        public string BlindingFactor { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }
    }

    /// <summary>
    /// Sends a confidential MPT amount to another holder's confidential inbox.
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public class ConfidentialMPTSend : TransactionRequest
    {
        public ConfidentialMPTSend()
        {
            TransactionType = TransactionType.ConfidentialMPTSend;
        }

        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        [JsonPropertyName("DestinationTag")]
        public uint? DestinationTag { get; set; }

        [JsonPropertyName("SenderEncryptedAmount")]
        public string SenderEncryptedAmount { get; set; }

        [JsonPropertyName("DestinationEncryptedAmount")]
        public string DestinationEncryptedAmount { get; set; }

        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        [JsonPropertyName("AmountCommitment")]
        public string AmountCommitment { get; set; }

        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }

        [JsonPropertyName("CredentialIDs")]
        public List<string> CredentialIDs { get; set; }
    }

    /// <inheritdoc cref="ConfidentialMPTSend" />
    public class ConfidentialMPTSendResponse : TransactionResponse
    {
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        [JsonPropertyName("DestinationTag")]
        public uint? DestinationTag { get; set; }

        [JsonPropertyName("SenderEncryptedAmount")]
        public string SenderEncryptedAmount { get; set; }

        [JsonPropertyName("DestinationEncryptedAmount")]
        public string DestinationEncryptedAmount { get; set; }

        [JsonPropertyName("IssuerEncryptedAmount")]
        public string IssuerEncryptedAmount { get; set; }

        [JsonPropertyName("AuditorEncryptedAmount")]
        public string AuditorEncryptedAmount { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }

        [JsonPropertyName("AmountCommitment")]
        public string AmountCommitment { get; set; }

        [JsonPropertyName("BalanceCommitment")]
        public string BalanceCommitment { get; set; }

        [JsonPropertyName("CredentialIDs")]
        public List<string> CredentialIDs { get; set; }
    }

    /// <summary>
    /// Claws back a confidential MPT amount from a holder (issuer only).
    /// </summary>
    /// <remarks>Requires the ConfidentialTransfer amendment. This feature is in draft and subject to change.</remarks>
    public class ConfidentialMPTClawback : TransactionRequest
    {
        public ConfidentialMPTClawback()
        {
            TransactionType = TransactionType.ConfidentialMPTClawback;
        }

        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    /// <inheritdoc cref="ConfidentialMPTClawback" />
    public class ConfidentialMPTClawbackResponse : TransactionResponse
    {
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; }

        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        [JsonPropertyName("MPTAmount")]
        public string MPTAmount { get; set; }

        [JsonPropertyName("ZKProof")]
        public string ZKProof { get; set; }
    }

    public partial class Validation
    {
        private static Task ValidateConfidentialCommon(Dictionary<string, object> tx, string txName)
        {
            if (!tx.TryGetValue("MPTokenIssuanceID", out var issuance) || issuance is not string)
                throw new ValidationException($"{txName}: missing field MPTokenIssuanceID");
            return Task.CompletedTask;
        }

        public static async Task ValidateConfidentialMPTConvert(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);
            await ValidateConfidentialCommon(tx, "ConfidentialMPTConvert");
        }

        public static async Task ValidateConfidentialMPTMergeInbox(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);
            await ValidateConfidentialCommon(tx, "ConfidentialMPTMergeInbox");
        }

        public static async Task ValidateConfidentialMPTConvertBack(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);
            await ValidateConfidentialCommon(tx, "ConfidentialMPTConvertBack");
        }

        public static async Task ValidateConfidentialMPTSend(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);
            await ValidateConfidentialCommon(tx, "ConfidentialMPTSend");
            if (!tx.TryGetValue("Destination", out var dest) || dest is not string)
                throw new ValidationException("ConfidentialMPTSend: missing field Destination");
        }

        public static async Task ValidateConfidentialMPTClawback(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);
            await ValidateConfidentialCommon(tx, "ConfidentialMPTClawback");
            if (!tx.TryGetValue("Holder", out var holder) || holder is not string)
                throw new ValidationException("ConfidentialMPTClawback: missing field Holder");
        }
    }
}
