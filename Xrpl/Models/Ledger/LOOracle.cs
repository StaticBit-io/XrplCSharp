using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/ledger-data/ledger-entry-types/oracle

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// An Oracle ledger entry holds data associated with a single price oracle,
    /// which can store information on up to 10 asset pairs.
    /// </summary>
    public class LOOracle : BaseLedgerEntry
    {
        /// <summary>
        /// Initializes a new instance of the LOOracle class.
        /// </summary>
        public LOOracle()
        {
            LedgerEntryType = LedgerEntryType.Oracle;
        }

        /// <summary>
        /// The XRPL account with update and delete privileges for the oracle.
        /// It's recommended to set up multi-signing on this account.
        /// </summary>
        [JsonPropertyName("Owner")]
        public string Owner { get; set; }

        /// <summary>
        /// An arbitrary value that identifies an oracle provider, such as Chainlink, Band, or DIA.
        /// This field is a string, up to 256 ASCII hex encoded characters (0x20-0x7E).
        /// </summary>
        [JsonPropertyName("Provider")]
        public string Provider { get; set; }

        /// <summary>
        /// Describes the type of asset, such as "currency", "commodity", or "index".
        /// Must be formatted as hexadecimal representing ASCII characters (0x20-0x7E), maximum 16 bytes.
        /// </summary>
        [JsonPropertyName("AssetClass")]
        public string AssetClass { get; set; }

        /// <summary>
        /// An array of up to 10 PriceData objects, each representing the price information for an asset pair.
        /// More than five PriceData objects require two owner reserves.
        /// </summary>
        [JsonPropertyName("PriceDataSeries")]
        public List<PriceDataWrapper> PriceDataSeries { get; set; }

        /// <summary>
        /// The time the data was last updated, represented in Unix time.
        /// Note: Unlike many other time values on the XRP Ledger, this value does not use the Ripple Epoch.
        /// </summary>
        [JsonPropertyName("LastUpdateTime")]
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTime? LastUpdateTime { get; set; }

        /// <summary>
        /// An optional Universal Resource Identifier to reference price data off-chain.
        /// This field is limited to 256 bytes.
        /// </summary>
        [JsonPropertyName("URI")]
        public string URI { get; set; }

        /// <summary>
        /// A hint indicating which page of the oracle owner's owner directory links to this entry,
        /// in case the directory consists of multiple pages.
        /// </summary>
        [JsonPropertyName("OwnerNode")]
        public string OwnerNode { get; set; }

        /// <summary>
        /// The hash of the previous transaction that modified this entry.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// The ledger index that this object was most recently modified or created in.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint PreviousTxnLgrSeq { get; set; }

        /// <summary>
        /// A bit-map of boolean flags. No flags are defined for the Oracle object type,
        /// so this value is always 0.
        /// </summary>
        [JsonPropertyName("Flags")]
        public uint Flags { get; set; }
    }
}
