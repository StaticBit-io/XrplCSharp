using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/subscription-methods/subscribe#book-changes-stream

namespace Xrpl.Models.Subscriptions
{
    /// <summary>
    /// The book_changes stream sends a message whenever a new ledger is validated,
    /// with a summary of all changes to order books in that ledger.
    /// <see href="https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/subscription-methods/subscribe#book-changes-stream"/>
    /// </summary>
    public class BookChangesStream : BaseStream
    {
        /// <summary>
        /// The ledger index of the validated ledger.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public uint LedgerIndex { get; set; }

        /// <summary>
        /// The identifying hash of the validated ledger.
        /// </summary>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }

        /// <summary>
        /// The close time of the validated ledger.
        /// </summary>
        [JsonPropertyName("ledger_time")]
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? LedgerTime { get; set; }

        /// <summary>
        /// If true, this data comes from a validated ledger.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }

        private List<BookChange> _changes = new List<BookChange>();

        /// <summary>
        /// A list of book changes in this ledger.
        /// </summary>
        [JsonPropertyName("changes")]
        public List<BookChange> Changes
        {
            get => _changes;
            set => _changes = value ?? new List<BookChange>();
        }
    }

    /// <summary>
    /// Represents a single order book change in a validated ledger.<br/>
    /// Volume and rate values are strings. When the corresponding asset is XRP,
    /// values are in drops (1 XRP = 1,000,000 drops).<br/>
    /// Rates (high, low, open, close) are expressed as CurrencyA per 1 unit of CurrencyB.
    /// </summary>
    public class BookChange
    {
        private const string XrpDrops = "XRP_drops";

        /// <summary>
        /// Raw currency identifier for the first asset.
        /// Format: "XRP_drops" for XRP, or "issuer/currency" for tokens, the currency as rippled
        /// displays it (a three-character code, or 40-character hex). Absent when the asset is an MPT.
        /// </summary>
        [JsonPropertyName("currency_a")]
        public string CurrencyA { get; set; }

        /// <summary>
        /// Raw currency identifier for the second asset, in the format of <see cref="CurrencyA"/>.
        /// Absent when the asset is an MPT.
        /// </summary>
        [JsonPropertyName("currency_b")]
        public string CurrencyB { get; set; }

        /// <summary>
        /// The MPT issuance ID of the first asset, when it is an MPT; <see cref="CurrencyA"/> is then absent.
        /// </summary>
        [JsonPropertyName("mpt_issuance_id_a")]
        public string MptIssuanceIdA { get; set; }

        /// <summary>
        /// The MPT issuance ID of the second asset, when it is an MPT; <see cref="CurrencyB"/> is then absent.
        /// </summary>
        [JsonPropertyName("mpt_issuance_id_b")]
        public string MptIssuanceIdB { get; set; }

        /// <summary>
        /// The permissioned domain of the order book, for offers in a permissioned DEX.
        /// </summary>
        [JsonPropertyName("domain")]
        public string Domain { get; set; }

        /// <summary>
        /// Parsed currency for the first asset.
        /// For XRP returns IssuedCurrency with Currency="XRP"; for tokens parses issuer and currency code;
        /// for an MPT carries <see cref="MptIssuanceIdA"/>.
        /// </summary>
        [JsonIgnore]
        public Common.Common.IssuedCurrency AssetA => ParseBookChangeAsset(CurrencyA, MptIssuanceIdA);

        /// <summary>
        /// Parsed currency for the second asset.
        /// For XRP returns IssuedCurrency with Currency="XRP"; for tokens parses issuer and currency code;
        /// for an MPT carries <see cref="MptIssuanceIdB"/>.
        /// </summary>
        [JsonIgnore]
        public Common.Common.IssuedCurrency AssetB => ParseBookChangeAsset(CurrencyB, MptIssuanceIdB);

        /// <summary>
        /// True if the first asset is XRP (values for VolumeA and rates are in drops).
        /// </summary>
        [JsonIgnore]
        public bool IsXrpA => CurrencyA == XrpDrops;

        /// <summary>
        /// True if the second asset is XRP (VolumeB is in drops).
        /// </summary>
        [JsonIgnore]
        public bool IsXrpB => CurrencyB == XrpDrops;

        /// <summary>
        /// Volume of the first asset traded (in drops when <see cref="IsXrpA"/> is true).
        /// </summary>
        [JsonPropertyName("volume_a")]
        public string VolumeA { get; set; }

        /// <summary>
        /// Volume of the second asset traded (in drops when <see cref="IsXrpB"/> is true).
        /// </summary>
        [JsonPropertyName("volume_b")]
        public string VolumeB { get; set; }

        /// <summary>
        /// High exchange rate: CurrencyA per 1 CurrencyB (in drops when <see cref="IsXrpA"/> is true).
        /// </summary>
        [JsonPropertyName("high")]
        public string High { get; set; }

        /// <summary>
        /// Low exchange rate: CurrencyA per 1 CurrencyB (in drops when <see cref="IsXrpA"/> is true).
        /// </summary>
        [JsonPropertyName("low")]
        public string Low { get; set; }

        /// <summary>
        /// Opening exchange rate: CurrencyA per 1 CurrencyB (in drops when <see cref="IsXrpA"/> is true).
        /// </summary>
        [JsonPropertyName("open")]
        public string Open { get; set; }

        /// <summary>
        /// Closing exchange rate: CurrencyA per 1 CurrencyB (in drops when <see cref="IsXrpA"/> is true).
        /// </summary>
        [JsonPropertyName("close")]
        public string Close { get; set; }

        /// <summary>
        /// Parses a book_changes currency string into an
        /// <see cref="Xrpl.Models.Common.Common.IssuedCurrency"/>.
        /// Handles "XRP_drops", "issuer/currency" and an MPT issuance ID.
        /// </summary>
        private static Common.Common.IssuedCurrency ParseBookChangeAsset(string raw, string mptIssuanceId)
        {
            if (!string.IsNullOrEmpty(mptIssuanceId))
                return new Common.Common.IssuedCurrency { MptIssuanceId = mptIssuanceId };


            if (string.IsNullOrEmpty(raw))
                return null;

            if (raw == XrpDrops)
                return new Common.Common.IssuedCurrency { Currency = "XRP" };

            int slashIndex = raw.IndexOf('/');
            if (slashIndex > 0 && slashIndex < raw.Length - 1)
            {
                return new Common.Common.IssuedCurrency
                {
                    Issuer = raw.Substring(0, slashIndex),
                    Currency = raw.Substring(slashIndex + 1)
                };
            }

            // Fallback: return raw as currency code
            return new Common.Common.IssuedCurrency { Currency = raw };
        }
    }
}
