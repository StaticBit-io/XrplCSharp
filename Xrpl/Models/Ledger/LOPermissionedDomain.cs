using System.Collections.Generic;

using Newtonsoft.Json;

using Xrpl.Models.Transactions;

// https://xrpl.org/docs/references/protocol/ledger-data/ledger-entry-types/permissioneddomain

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// A PermissionedDomain ledger entry describes a single permissioned domain instance.
    /// You can create a permissioned domain by sending a PermissionedDomainSet transaction.
    /// Requires the PermissionedDomains amendment.
    /// </summary>
    public class LOPermissionedDomain : BaseLedgerEntry
    {
        /// <summary>
        /// Initializes a new instance of the LOPermissionedDomain class.
        /// </summary>
        public LOPermissionedDomain()
        {
            LedgerEntryType = LedgerEntryType.PermissionedDomain;
        }

        /// <summary>
        /// The address of the account that owns this domain.
        /// </summary>
        [JsonProperty("Owner")]
        public string Owner { get; set; }

        /// <summary>
        /// A hint indicating which page of the owner directory links to this entry,
        /// in case the directory consists of multiple pages.
        /// </summary>
        [JsonProperty("OwnerNode")]
        public string OwnerNode { get; set; }

        /// <summary>
        /// The Sequence value of the transaction that created this entry.
        /// </summary>
        [JsonProperty("Sequence")]
        public uint Sequence { get; set; }

        /// <summary>
        /// A list of 1 to 10 Credential objects that grant access to this domain.
        /// The array is stored sorted by issuer.
        /// </summary>
        [JsonProperty("AcceptedCredentials")]
        public List<AcceptedCredentialWrapper> AcceptedCredentials { get; set; }

        /// <summary>
        /// The identifying hash of the transaction that most recently modified this entry.
        /// </summary>
        [JsonProperty("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// The index of the ledger that contains the transaction that most recently modified this object.
        /// </summary>
        [JsonProperty("PreviousTxnLgrSeq")]
        public uint PreviousTxnLgrSeq { get; set; }
    }
}
