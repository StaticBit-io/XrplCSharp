using System.Text.Json.Serialization;

// https://xrpl.org/docs/references/protocol/ledger-data/ledger-entry-types/did

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// A DID ledger entry holds references to, or data associated with,
    /// a single DID (Decentralized Identifier).
    /// </summary>
    public class LODID : BaseLedgerEntry
    {
        /// <summary>
        /// Initializes a new instance of the LODID class.
        /// </summary>
        public LODID()
        {
            LedgerEntryType = LedgerEntryType.DID;
        }

        /// <summary>
        /// The account that controls the DID.
        /// </summary>
        [JsonPropertyName("Account")]
        public string Account { get; set; }

        /// <summary>
        /// The public attestations of identity credentials associated with the DID.
        /// </summary>
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <summary>
        /// The DID document associated with the DID.
        /// </summary>
        [JsonPropertyName("DIDDocument")]
        public string DIDDocument { get; set; }

        /// <summary>
        /// The Universal Resource Identifier associated with the DID.
        /// </summary>
        [JsonPropertyName("URI")]
        public string URI { get; set; }

        /// <summary>
        /// A hint indicating which page of the owner directory links to this entry,
        /// in case the directory consists of multiple pages.
        /// </summary>
        [JsonPropertyName("OwnerNode")]
        public string OwnerNode { get; set; }

        /// <summary>
        /// The identifying hash of the transaction that most recently modified this entry.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// The index of the ledger that contains the transaction that most recently modified this entry.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint PreviousTxnLgrSeq { get; set; }
    }
}
