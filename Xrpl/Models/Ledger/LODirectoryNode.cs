using System.Collections.Generic;


// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/ledger/DirectoryNode.ts

using System.Text.Json.Serialization;

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// Flags of a DirectoryNode ledger object.
    /// </summary>
    /// <remarks>
    /// <see cref="LODirectoryNode.Flags"/> stays a raw <c>uint</c> for backwards compatibility, but is
    /// nullable (absent e.g. inside PreviousFields). A lifted <c>!=</c> returns true when either side
    /// is null, so check presence explicitly: <c>dir.Flags is { } f &amp;&amp; (f &amp; (uint)DirectoryNodeFlags.lsfNFTokenBuyOffers) != 0</c>.
    /// </remarks>
    [System.Flags]
    public enum DirectoryNodeFlags : uint
    {
        /// <summary>
        /// The directory holds buy offers for an NFToken.
        /// </summary>
        lsfNFTokenBuyOffers = 0x00000001,

        /// <summary>
        /// The directory holds sell offers for an NFToken.
        /// </summary>
        lsfNFTokenSellOffers = 0x00000002,
    }

    /// <summary>
    /// The DirectoryNode object type provides a list of links to other objects in the ledger's state tree.
    /// </summary>
    public class LODirectoryNode : BaseLedgerEntry
    {

        /// <summary>
        /// A bit-map of boolean flags enabled for this directory.
        /// See <see cref="DirectoryNodeFlags"/> for the values the protocol defines.
        /// </summary>
        public uint? Flags { get; set; }
        /// <summary>
        /// The ID of root object for this directory.
        /// </summary>
        public string RootIndex { get; set; }
        /// <summary>
        /// The contents of this Directory: an array of IDs of other objects.
        /// </summary>
        public List<string> Indexes { get; set; }
        /// <summary>
        /// If this Directory consists of multiple pages,
        /// this ID links to the next object in the chain, wrapping around at the end.
        /// </summary>
        public string IndexNext { get; set; }
        /// <summary>
        /// If this Directory consists of multiple pages,
        /// this ID links to the previous object in the chain, wrapping around at the beginning.
        /// </summary>
        public string IndexPrevious { get; set; }
        /// <summary>
        /// The address of the account that owns the objects in this directory.
        /// </summary>
        public string Owner { get; set; }
        /// <summary>
        /// The currency code of the TakerPays amount from the offers in this directory.
        /// </summary>
        public string TakerPaysCurrency { get; set; }
        /// <summary>
        /// The issuer of the TakerPays amount from the offers in this directory. 
        /// </summary>
        public string TakerPaysIssuer { get; set; }
        /// <summary>
        /// The currency code of the TakerGets amount from the offers in this directory.
        /// </summary>
        public string TakerGetsCurrency { get; set; }
        /// <summary>
        /// The issuer of the TakerGets amount from the offers in this directory.
        /// </summary>
        public string TakerGetsIssuer { get; set; }
    
    /// <summary>PermissionedDEX: the domain this order book belongs to.</summary>
    [JsonPropertyName("DomainID")]
    public string DomainID { get; set; }

    /// <summary>Order book directories: the exchange rate portion of the directory index (hex UInt64).</summary>
    [JsonPropertyName("ExchangeRate")]
    public string ExchangeRate { get; set; }

    /// <summary>NFT offer directories: the NFToken this directory relates to.</summary>
    [JsonPropertyName("NFTokenID")]
    public string NFTokenID { get; set; }

    /// <summary>MPT order books: MPT issuance id on the TakerPays side.</summary>
    [JsonPropertyName("TakerPaysMPT")]
    public string TakerPaysMPT { get; set; }

    /// <summary>MPT order books: MPT issuance id on the TakerGets side.</summary>
    [JsonPropertyName("TakerGetsMPT")]
    public string TakerGetsMPT { get; set; }

    /// <summary>
    /// The identifying hash of the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnID")]
    public string PreviousTxnID { get; set; }

    /// <summary>
    /// The index of the ledger that contains the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnLgrSeq")]
    public uint? PreviousTxnLgrSeq { get; set; }
}
}
