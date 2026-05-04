using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/oracleset

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Creates a new Oracle ledger entry or updates the fields of an existing one,
    /// using the Oracle ID.
    /// </summary>
    public interface IOracleSet : ITransactionCommon
    {
        /// <summary>
        /// A unique identifier of the price oracle for the Account.
        /// </summary>
        uint OracleDocumentID { get; set; }

        /// <summary>
        /// The time the data was last updated, represented as a unix timestamp in seconds.
        /// The value must be within 300 seconds (5 minutes) of the ledger's close time.
        /// </summary>
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTime? LastUpdateTime { get; set; }

        /// <summary>
        /// An array of up to 10 PriceData objects, each representing the price information
        /// for a token pair. More than five PriceData objects require two owner reserves.
        /// </summary>
        List<PriceDataWrapper> PriceDataSeries { get; set; }

        /// <summary>
        /// An arbitrary value that identifies an oracle provider, such as Chainlink, Band,
        /// or DIA. This field is a string, up to 256 ASCII hex encoded characters (0x20-0x7E).
        /// This field is required when creating a new Oracle ledger entry, but is optional for updates.
        /// </summary>
        string Provider { get; set; }

        /// <summary>
        /// An optional Universal Resource Identifier to reference price data off-chain.
        /// This field is limited to 256 bytes.
        /// </summary>
        string URI { get; set; }

        /// <summary>
        /// Describes the type of asset, such as "currency", "commodity", or "index".
        /// This field is a string, up to 16 ASCII hex encoded characters (0x20-0x7E).
        /// This field is required when creating a new Oracle ledger entry, but is optional for updates.
        /// </summary>
        string AssetClass { get; set; }
    }

    /// <inheritdoc cref="IOracleSet" />
    public class OracleSet : TransactionRequest, IOracleSet
    {
        /// <summary>
        /// Initializes a new instance of the OracleSet class.
        /// </summary>
        public OracleSet()
        {
            TransactionType = TransactionType.OracleSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("OracleDocumentID")]
        public uint OracleDocumentID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LastUpdateTime")]
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTime? LastUpdateTime { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PriceDataSeries")]
        public List<PriceDataWrapper> PriceDataSeries { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Provider")]
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string Provider { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("URI")]
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string URI { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AssetClass")]
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string AssetClass { get; set; }
    }

    /// <inheritdoc cref="IOracleSet" />
    public class OracleSetResponse : TransactionResponse, IOracleSet
    {
        /// <inheritdoc />
        [JsonPropertyName("OracleDocumentID")]
        public uint OracleDocumentID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LastUpdateTime")]
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTime? LastUpdateTime { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PriceDataSeries")]
        public List<PriceDataWrapper> PriceDataSeries { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Provider")]
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string Provider { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("URI")]
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string URI { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AssetClass")]
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string AssetClass { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Maximum number of PriceData objects in PriceDataSeries.
        /// </summary>
        public const int ORACLE_PRICE_DATA_SERIES_MAX_LENGTH = 10;

        /// <summary>
        /// Maximum scale value for PriceData.
        /// </summary>
        public const int ORACLE_SCALE_MAX = 10;

        /// <summary>
        /// Verify the form and type of an OracleSet at runtime.
        /// </summary>
        /// <param name="tx">An OracleSet Transaction.</param>
        /// <exception cref="ValidationException">When the OracleSet is malformed.</exception>
        public static async Task ValidateOracleSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("OracleDocumentID", out var oracleDocumentID) || oracleDocumentID is null)
                throw new ValidationException("OracleSet: missing field OracleDocumentID");

            if (!tx.TryGetValue("LastUpdateTime", out var lastUpdateTime) || lastUpdateTime is null)
                throw new ValidationException("OracleSet: missing field LastUpdateTime");

            if (!tx.TryGetValue("PriceDataSeries", out var priceDataSeries) || priceDataSeries is null)
                throw new ValidationException("OracleSet: missing field PriceDataSeries");

            if (priceDataSeries is not IList<object> priceDataList)
                throw new ValidationException("OracleSet: PriceDataSeries must be an array");

            if (priceDataList.Count == 0)
                throw new ValidationException("OracleSet: PriceDataSeries must not be empty");

            if (priceDataList.Count > ORACLE_PRICE_DATA_SERIES_MAX_LENGTH)
                throw new ValidationException($"OracleSet: PriceDataSeries must have at most {ORACLE_PRICE_DATA_SERIES_MAX_LENGTH} PriceData objects");

            foreach (var priceDataWrapper in priceDataList)
            {
                if (priceDataWrapper is not Dictionary<string, object> wrapper)
                    throw new ValidationException("OracleSet: PriceDataSeries must be an array of objects");

                if (!wrapper.TryGetValue("PriceData", out var priceData) || priceData is null)
                    throw new ValidationException("OracleSet: PriceDataSeries must have a PriceData object");

                if (priceData is not Dictionary<string, object> priceDataDict)
                    throw new ValidationException("OracleSet: PriceData must be an object");

                if (!priceDataDict.TryGetValue("BaseAsset", out var baseAsset) || baseAsset is not string)
                    throw new ValidationException("OracleSet: PriceData must have a BaseAsset string");

                if (!priceDataDict.TryGetValue("QuoteAsset", out var quoteAsset) || quoteAsset is not string)
                    throw new ValidationException("OracleSet: PriceData must have a QuoteAsset string");

                bool hasAssetPrice = priceDataDict.TryGetValue("AssetPrice", out var assetPrice) && assetPrice is not null;
                bool hasScale = priceDataDict.TryGetValue("Scale", out var scale) && scale is not null;

                if (hasAssetPrice != hasScale)
                    throw new ValidationException("OracleSet: PriceData must have both AssetPrice and Scale if any are present");

                if (hasScale)
                {
                    int scaleValue;
                    try
                    {
                        scaleValue = Convert.ToInt32(scale);
                    }
                    catch
                    {
                        throw new ValidationException("OracleSet: Scale must be a number");
                    }

                    if (scaleValue < 0 || scaleValue > ORACLE_SCALE_MAX)
                        throw new ValidationException($"OracleSet: Scale must be in range 0-{ORACLE_SCALE_MAX}");
                }
            }

            if (tx.TryGetValue("Provider", out var provider) && provider is not null && provider is not string)
                throw new ValidationException("OracleSet: Provider must be a string");

            if (tx.TryGetValue("URI", out var uri) && uri is not null && uri is not string)
                throw new ValidationException("OracleSet: URI must be a string");

            if (tx.TryGetValue("AssetClass", out var assetClass) && assetClass is not null && assetClass is not string)
                throw new ValidationException("OracleSet: AssetClass must be a string");
        }
    }
}
