using Newtonsoft.Json;

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
    //    [JsonProperty("currency")]
    //    public string Currency { get; set; }
    //    /// <summary>
    //    /// Currency Issuer
    //    /// </summary>
    //    [JsonProperty("issuer")]
    //    public string Issuer { get; set; }
    //}

    /// <summary> common class </summary>
    public class Common
    {
        /// <summary> is XRP currency </summary>
        public class XRP
        {
            /// <summary> XRP currency code </summary>
            [JsonProperty("currency")]
            public string Currency = "XRP";
        }

        /// <summary> currency with issuer </summary>
        public class IssuedCurrency
        {
            /// <summary>
            /// currency code
            /// </summary>
            [JsonProperty("currency")]
            public string Currency { get; set; }

            /// <summary>
            /// currency issuer
            /// </summary>
            [JsonProperty("issuer")]
            public string Issuer { get; set; }

            public bool IsXrp()
            {
                return Issuer is null;
            }

            public override string ToString() => $"{Currency.CurrencyReadableName()}";
        }

        /// <summary> currency with amount and issuer </summary>
        public class IssuedCurrencyAmount : IssuedCurrency
        {
            /// <summary> currency value </summary>
            [JsonProperty("value")]
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
            [JsonProperty("mpt_issuance_id")]
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
            [JsonProperty("value")]
            public string Value { get; set; }
        }
    }
}