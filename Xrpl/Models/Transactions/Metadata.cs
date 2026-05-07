using System.Text.Json.Serialization;

using System.Collections.Generic;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

//https://github.com/XRPLF/xrpl.js/blob/45963b70356f4609781a6396407e2211fd15bcf1/packages/xrpl/src/models/transactions/metadata.ts#L32
namespace Xrpl.Models.Transactions;

public interface ICreatedNode
{
    LedgerEntryType LedgerEntryType { get; set; }

    string LedgerIndex { get; set; }

    BaseLedgerEntry NewFields { get; set; }
}

public interface IModifiedNode
{
    LedgerEntryType LedgerEntryType { get; set; }

    string LedgerIndex { get; set; }

    BaseLedgerEntry FinalFields { get; set; }

    BaseLedgerEntry? PreviousFields { get; set; }

    string PreviousTxnID { get; set; }

    uint? PreviousTxnLgrSeq { get; set; }
}

public interface IDeletedNode
{
    LedgerEntryType LedgerEntryType { get; set; }

    string LedgerIndex { get; set; }

    BaseLedgerEntry FinalFields { get; set; }

    BaseLedgerEntry PreviousFields { get; set; }
}

public interface IAffectedNode
{
    /// <summary>
    /// Gets or sets the created node information.
    /// </summary>
    CreatedNode CreatedNode { get; set; }

    /// <summary>
    /// Gets or sets the modified node information.
    /// </summary>
    ModifiedNode ModifiedNode { get; set; }

    /// <summary>
    /// Gets or sets the deleted node information.
    /// </summary>
    DeletedNode DeletedNode { get; set; }
}

public interface ITransactionMetadata
{
    List<AffectedNode> AffectedNodes { get; set; }

    /// <summary>
    /// Gets or sets the amount that was actually delivered.
    /// </summary>
    [JsonConverter(typeof(CurrencyConverter))]
    [JsonPropertyName("DeliveredAmount")]
    Currency PartialDeliveredAmount { get; set; }

    /// <summary>
    /// Gets or sets the delivered amount (may be 'unavailable' for transactions before 2014-01-20).
    /// </summary>
    [JsonConverter(typeof(CurrencyConverter))]
    [JsonPropertyName("delivered_amount")]
    Currency ActuallyDeliveredAmount { get; set; }

    /// <summary>
    /// Gets or sets the transaction index.
    /// </summary>
    int TransactionIndex { get; set; }

    /// <summary>
    /// Gets or sets the transaction result.
    /// </summary>
    string TransactionResult { get; set; }

    /// <summary>
    /// Shows the OfferIDof a new NFTokenOffer in a response from a NFTokenCreateOffer transaction.
    /// </summary>
    [JsonPropertyName("offer_id")]
    public string OfferID { get; set; }

    /// <summary>
    /// Shows the NFTokenID for the NFToken that changed on the ledger as a result of the transaction.
    /// Only present if the transaction is NFTokenMint or NFTokenAcceptOffer
    /// </summary>
    [JsonPropertyName("nftoken_id")]
    public string NFTokenId { get; set; }

    /// <summary>
    /// Shows all the NFTokenIDs for the NFTokens that changed on the ledger as a result of the transaction.
    /// Only present if the transaction is NFTokenCancelOffer.
    /// </summary>
    [JsonPropertyName("nftoken_ids")]
    public string[] NFTokenIds { get; set; }

    /// <summary>
    /// Batch ID of the batch that this transaction belongs to, if any.
    /// </summary>
    public string? ParentBatchID { get; set; }

    /// <summary>
    /// Shows the MPTokenIssuanceID for the MPTokenIssuance that was created by this transaction.
    /// Only present if the transaction is MPTokenIssuanceCreate.
    /// </summary>
    [JsonPropertyName("mpt_issuance_id")]
    public string MptIssuanceId { get; set; }
}