using Newtonsoft.Json;

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

    dynamic NewFields { get; set; }

    public BaseLedgerEntry New { get; }
}

public interface IModifiedNode
{
    LedgerEntryType LedgerEntryType { get; set; }

    string LedgerIndex { get; set; }

    dynamic FinalFields { get; set; }

    public BaseLedgerEntry Final { get; }

    dynamic PreviousFields { get; set; }

    public BaseLedgerEntry Previous { get; }

    string PreviousTxnID { get; set; }

    uint? PreviousTxnLgrSeq { get; set; }
}

public interface IDeletedNode
{
    LedgerEntryType LedgerEntryType { get; set; }

    string LedgerIndex { get; set; }

    dynamic FinalFields { get; set; }

    public BaseLedgerEntry Final { get; }

    dynamic PreviousFields { get; set; }

    public BaseLedgerEntry Previous { get; }
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
    [JsonProperty("DeliveredAmount")]
    Currency PartialDeliveredAmount { get; set; }

    /// <summary>
    /// Gets or sets the delivered amount (may be 'unavailable' for transactions before 2014-01-20).
    /// </summary>
    [JsonConverter(typeof(CurrencyConverter))]
    [JsonProperty("delivered_amount")]
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
    [JsonProperty("offer_id")]
    public string OfferID { get; set; }

    /// <summary>
    /// Shows the NFTokenID for the NFToken that changed on the ledger as a result of the transaction.
    /// Only present if the transaction is NFTokenMint or NFTokenAcceptOffer
    /// </summary>
    [JsonProperty("nftoken_id")]
    public string NFTokenId { get; set; }

    /// <summary>
    /// Shows all the NFTokenIDs for the NFTokens that changed on the ledger as a result of the transaction.
    /// Only present if the transaction is NFTokenCancelOffer.
    /// </summary>
    [JsonProperty("nftoken_ids")]
    public string[] NFTokenIds { get; set; }
}