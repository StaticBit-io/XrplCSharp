using System.Text.Json.Serialization;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/common/index.ts

namespace Xrpl.Models.Common
{
    ///// <summary>
    ///// Order book currency
    ///// </summary>
    //public class Currency
    //{
    //    /// <summary>
    //    /// Currency code
    //    /// </summary>
    //    [JsonPropertyName("currency")]
    //    public string Currency { get; set; }
    //    /// <summary>
    //    /// Currency Issuer
    //    /// </summary>
    //    [JsonPropertyName("issuer")]
    //    public string Issuer { get; set; }
    //}

    /// <summary> common class </summary>
    public class Common
    {
        /// <summary> is XRP currency </summary>
        public class XRP
        {
            /// <summary> XRP currency code </summary>
            [JsonPropertyName("currency")]
            public string Currency = "XRP";
        }

        /// <summary> currency with issuer </summary>
        public class IssuedCurrency
        {
            /// <summary>
            /// currency code
            /// </summary>
            [JsonPropertyName("currency")]
            public string Currency { get; set; }

            /// <summary>
            /// Readable assert name 
            /// </summary>
            [JsonIgnore]
            public string CurrencyName => Currency.CurrencyReadableName();

            /// <summary>
            /// currency issuer
            /// </summary>
            [JsonPropertyName("issuer")]
            public string Issuer { get; set; }

            /// <summary>
            /// MPT issuance identifier (XLS-33). When set, Currency and Issuer are ignored.
            /// </summary>
            [JsonPropertyName("mpt_issuance_id")]
            public string MptIssuanceId { get; set; }

            public bool IsXrp()
            {
                return Issuer is null && MptIssuanceId is null;
            }

            /// <summary>
            /// Returns true if this represents an MPT issue.
            /// </summary>
            public bool IsMpt()
            {
                return !string.IsNullOrWhiteSpace(MptIssuanceId);
            }

            public override string ToString() => !string.IsNullOrWhiteSpace(MptIssuanceId)
                ? $"MPT:{MptIssuanceId}"
                : $"{Currency.CurrencyReadableName()}";
        }

        /// <summary> currency with amount and issuer </summary>
        public class IssuedCurrencyAmount : IssuedCurrency
        {
            /// <summary> currency value </summary>
            [JsonPropertyName("value")]
            public string Value { get; set; }
        }

        /// <summary>
        /// Represents a Multi-Purpose Token (MPT) currency identifier.
        /// </summary>
        public class MPTCurrency
        {
            /// <summary>
            /// The unique identifier for the MPT issuance.
            /// </summary>
            [JsonPropertyName("mpt_issuance_id")]
            public string MptIssuanceId { get; set; }
        }

        /// <summary>
        /// Represents a Multi-Purpose Token (MPT) amount with value.
        /// </summary>
        public class MPTAmount : MPTCurrency
        {
            /// <summary>
            /// The amount of MPT tokens.
            /// </summary>
            [JsonPropertyName("value")]
            public string Value { get; set; }
        }
    }
}