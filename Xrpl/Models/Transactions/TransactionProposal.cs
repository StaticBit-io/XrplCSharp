using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Posts an unsigned transaction on-ledger as a TransactionProposal, where signatures for it
    /// are collected until it is fully signed and anyone may submit it.
    /// </summary>
    /// <remarks>
    /// The proposed transaction has to pass the stateless checks it would face if submitted
    /// directly, must be ticket-based (<c>Sequence</c> 0 and a <c>TicketSequence</c>), must carry an
    /// empty <c>SigningPubKey</c> and no signature fields, and cannot be another proposal, a
    /// pseudo-transaction or a batch inner transaction.
    /// Not delegable. Requires the Cosign amendment. This feature is in draft and subject to change.
    /// </remarks>
    public interface ITransactionProposalCreate : ITransactionCommon
    {
        /// <summary>
        /// The transaction being proposed, in its unsigned canonical form.
        /// </summary>
        ITransactionRequest ProposedTransaction { get; set; }

        /// <summary>
        /// The time after which the proposal can no longer collect signatures, in seconds since
        /// the Ripple Epoch. Must not be 0.
        /// </summary>
        uint? Expiration { get; set; }
    }

    /// <inheritdoc cref="ITransactionProposalCreate" />
    public class TransactionProposalCreate : TransactionRequest, ITransactionProposalCreate
    {
        public TransactionProposalCreate()
        {
            TransactionType = TransactionType.TransactionProposalCreate;
        }

        /// <inheritdoc />
        [JsonPropertyName("ProposedTransaction")]
        [JsonConverter(typeof(TransactionRequestConverter))]
        public ITransactionRequest ProposedTransaction { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Expiration")]
        public uint? Expiration { get; set; }
    }

    /// <inheritdoc cref="ITransactionProposalCreate" />
    public class TransactionProposalCreateResponse : TransactionResponse, ITransactionProposalCreate
    {
        /// <inheritdoc />
        [JsonPropertyName("ProposedTransaction")]
        [JsonConverter(typeof(TransactionRequestConverter))]
        public ITransactionRequest ProposedTransaction { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Expiration")]
        public uint? Expiration { get; set; }
    }

    /// <summary>
    /// Deletes a TransactionProposal and releases its owner's reserve.
    /// </summary>
    /// <remarks>
    /// While the proposal is live only its owner or the account the proposed transaction is for
    /// may cancel it; once it is terminal anyone may.
    /// Not delegable. Requires the Cosign amendment. This feature is in draft and subject to change.
    /// </remarks>
    public interface ITransactionProposalCancel : ITransactionCommon
    {
        /// <summary>
        /// The ledger index of the TransactionProposal to delete. Must not be zero.
        /// </summary>
        string ProposalID { get; set; }
    }

    /// <inheritdoc cref="ITransactionProposalCancel" />
    public class TransactionProposalCancel : TransactionRequest, ITransactionProposalCancel
    {
        public TransactionProposalCancel()
        {
            TransactionType = TransactionType.TransactionProposalCancel;
        }

        /// <inheritdoc />
        [JsonPropertyName("ProposalID")]
        public string ProposalID { get; set; }
    }

    /// <inheritdoc cref="ITransactionProposalCancel" />
    public class TransactionProposalCancelResponse : TransactionResponse, ITransactionProposalCancel
    {
        /// <inheritdoc />
        [JsonPropertyName("ProposalID")]
        public string ProposalID { get; set; }
    }

    public partial class Validation
    {
        private const string ZeroHash256 = "0000000000000000000000000000000000000000000000000000000000000000";

        public static void ValidateTransactionProposalCreate(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("ProposedTransaction", out var proposed) || proposed is not IDictionary<string, object> proposedTx)
                throw new ValidationException("TransactionProposalCreate: missing field ProposedTransaction");

            if (!tx.TryGetValue("Expiration", out var expiration) || !Common.TryGetUInt32(expiration, out uint expirationValue))
                throw new ValidationException("TransactionProposalCreate: missing field Expiration");
            if (expirationValue == 0)
                throw new ValidationException("TransactionProposalCreate: Expiration must not be 0");

            // Mirror of the stateless part of rippled TransactionProposalCreate::preflight that
            // needs no ledger and no inner preflight: the proposal carries no signature, an empty
            // SigningPubKey, and is ticket-based so target-account activity cannot invalidate it.
            if (proposedTx.ContainsKey("TxnSignature") || proposedTx.ContainsKey("Signers"))
                throw new ValidationException("TransactionProposalCreate: ProposedTransaction must not carry signature fields");
            if (!proposedTx.TryGetValue("SigningPubKey", out var pubKey) || pubKey is not string { Length: 0 })
                throw new ValidationException("TransactionProposalCreate: ProposedTransaction SigningPubKey must be present and empty");
            if (!proposedTx.ContainsKey("TicketSequence")
                || !proposedTx.TryGetValue("Sequence", out var sequence)
                || !Common.TryGetUInt32(sequence, out uint sequenceValue)
                || sequenceValue != 0)
            {
                throw new ValidationException("TransactionProposalCreate: ProposedTransaction must use a TicketSequence with Sequence 0");
            }
        }

        public static void ValidateTransactionProposalCancel(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("ProposalID", out var proposalId) || proposalId is not string id)
                throw new ValidationException("TransactionProposalCancel: missing field ProposalID");
            if (string.Equals(id, ZeroHash256, System.StringComparison.Ordinal))
                throw new ValidationException("TransactionProposalCancel: ProposalID must not be zero");
        }
    }
}
