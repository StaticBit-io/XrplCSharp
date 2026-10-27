using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transactions;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A TransactionProposal ledger object holds an unsigned transaction while signatures for it
/// are collected.
/// </summary>
/// <remarks>
/// Signatures accumulate on <see cref="ProposedTransaction"/>. It is fully signed once its own
/// account (or that account's delegate) signs it, or once the weight in its <c>Signers</c> meets
/// that account's quorum; from then on anyone may copy and submit it.
/// Requires the Cosign amendment. This feature is in draft and subject to change.
/// </remarks>
public class LOTransactionProposal : BaseLedgerEntry
{
    /// <summary>
    /// The account that created the proposal and holds its reserve.
    /// </summary>
    [JsonPropertyName("Owner")]
    public string Owner { get; init; }

    /// <summary>
    /// The proposed transaction, with the signatures collected so far.
    /// </summary>
    [JsonPropertyName("ProposedTransaction")]
    [JsonConverter(typeof(TransactionRequestConverter))]
    public ITransactionRequest ProposedTransaction { get; init; }

    /// <summary>
    /// The time after which the proposal can no longer collect signatures, in seconds since the Ripple Epoch.
    /// </summary>
    [JsonPropertyName("Expiration")]
    public uint? Expiration { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object.
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// The identifying hash of the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnID")]
    public string PreviousTxnID { get; init; }

    /// <summary>
    /// The index of the ledger that contains the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnLgrSeq")]
    public uint? PreviousTxnLgrSeq { get; init; }
}
