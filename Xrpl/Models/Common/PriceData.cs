using Newtonsoft.Json;
using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Common
{
    /// <summary>
    /// Represents price information for a token pair in a price oracle.
    /// </summary>
    public class PriceDataWrapper
    {
        /// <summary>
        /// The PriceData object containing the price information.
        /// </summary>
        [JsonProperty("PriceData")]
        public PriceData PriceData { get; set; }
    }

    /// <summary>
    /// Represents the price information for a single asset pair in a price oracle.
    /// </summary>
    public class PriceData
    {
        /// <summary>
        /// The primary asset in a trading pair. Any valid identifier, such as a stock symbol,
        /// bond CUSIP, or currency code is allowed. For example, in the BTC/USD pair, BTC is
        /// the base asset; in 912810RR9/BTC, 912810RR9 is the base asset.
        /// Serialized as a 40-character hex string for XRPL protocol.
        /// </summary>
        [JsonProperty("BaseAsset")]
        [JsonConverter(typeof(OracleCurrencyConverter))]
        public string BaseAsset { get; set; }

        /// <summary>
        /// The quote asset in a trading pair. The quote asset denotes the price of one unit
        /// of the base asset. For example, in the BTC/USD pair, USD is the quote asset.
        /// Serialized as a 40-character hex string for XRPL protocol.
        /// </summary>
        [JsonProperty("QuoteAsset")]
        [JsonConverter(typeof(OracleCurrencyConverter))]
        public string QuoteAsset { get; set; }

        /// <summary>
        /// The asset price after applying the Scale precision level. It's not included if
        /// the last update transaction didn't include the BaseAsset/QuoteAsset pair.
        /// Serialized as a lowercase hexadecimal string for XRPL protocol.
        /// </summary>
        [JsonProperty("AssetPrice", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(AssetPriceConverter))]
        public object AssetPrice { get; set; }

        /// <summary>
        /// The scaling factor to apply to an asset price. For example, if Scale is 6 and
        /// original price is 0.155, then the scaled price is 155000. Valid scale ranges
        /// are 0-10. It's not included if the last update transaction didn't include
        /// the BaseAsset/QuoteAsset pair.
        /// </summary>
        [JsonProperty("Scale", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public uint? Scale { get; set; }
    }
}
