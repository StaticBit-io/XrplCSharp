// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/getAggregatePrice.ts
// https://github.com/XRPLF/rippled/blob/develop/src/xrpld/rpc/handlers/orderbook/GetAggregatePrice.cpp

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Methods;

/// <summary>
/// The <c>get_aggregate_price</c> method retrieves the aggregate price of the given
/// <c>Oracle</c> objects: the mean, the median and, when <see cref="Trim"/> is set, the trimmed mean.
/// Returns a <see cref="GetAggregatePriceResponse"/>.
/// </summary>
/// <remarks>
/// For each oracle the node takes the price of the <see cref="BaseAsset"/>/<see cref="QuoteAsset"/> pair
/// from the oracle's current <c>PriceDataSeries</c>, or from up to three earlier versions of it when the
/// latest update did not carry the pair. Oracles that never priced the pair are skipped; the request
/// fails with <c>objectNotFound</c> when none did.
/// </remarks>
/// <code>
/// {
///   "command": "get_aggregate_price",
///   "ledger_index": "current",
///   "base_asset": "XRP",
///   "quote_asset": "USD",
///   "trim": 20,
///   "oracles": [
///     { "account": "rNZ9m6AP9K7z3EVg6GhPMx36V4QmZKeWds", "oracle_document_id": 34 }
///   ]
/// }
/// </code>
public class GetAggregatePriceRequest : BaseLedgerRequest
{
    /// <summary>
    /// The largest number of oracles rippled accepts in one request.
    /// </summary>
    public const int MaxOracles = 200;

    /// <summary>
    /// The largest <see cref="Trim"/> percentage rippled accepts.
    /// </summary>
    public const uint MaxTrim = 25;

    /// <summary>
    /// Creates an empty request; set <see cref="BaseAsset"/>, <see cref="QuoteAsset"/> and <see cref="Oracles"/>.
    /// </summary>
    public GetAggregatePriceRequest() { Command = "get_aggregate_price"; }

    /// <summary>
    /// Creates a request for the price of <paramref name="baseAsset"/> in <paramref name="quoteAsset"/> across <paramref name="oracles"/>.
    /// </summary>
    public GetAggregatePriceRequest(string baseAsset, string quoteAsset, IEnumerable<OracleIdentifier> oracles) : this()
    {
        BaseAsset = baseAsset;
        QuoteAsset = quoteAsset;
        Oracles = new List<OracleIdentifier>(oracles ?? throw new ArgumentNullException(nameof(oracles)));
    }

    /// <summary>
    /// The currency code of the asset to be priced, as given in the oracles' <c>PriceData.BaseAsset</c>:
    /// a three-character code such as <c>XRP</c>, a longer name such as <c>RLUSD</c>, or a 40-character hex code.
    /// </summary>
    /// <remarks>
    /// The node matches the pair by comparing this string with its own display form of the stored currency,
    /// so the SDK writes the code in that form: a longer name and a lowercase hex code become uppercase hex,
    /// and a hex code holding a standard three-character code becomes those three characters.
    /// </remarks>
    [JsonPropertyName("base_asset")]
    [JsonConverter(typeof(AggregatePriceAssetConverter))]
    public string BaseAsset { get; set; }

    /// <summary>
    /// The currency code the price of the base asset is quoted in, in the same forms as <see cref="BaseAsset"/>.
    /// </summary>
    [JsonPropertyName("quote_asset")]
    [JsonConverter(typeof(AggregatePriceAssetConverter))]
    public string QuoteAsset { get; set; }

    /// <summary>
    /// The oracles to aggregate, between 1 and <see cref="MaxOracles"/>. A repeated oracle is counted once.
    /// </summary>
    [JsonPropertyName("oracles")]
    public List<OracleIdentifier> Oracles { get; set; } = new List<OracleIdentifier>();

    /// <summary>
    /// The percentage of outliers to trim from each end of the sorted prices, from 1 to <see cref="MaxTrim"/>.
    /// When set, the response carries <see cref="GetAggregatePriceResponse.TrimmedSet"/>.
    /// </summary>
    [JsonPropertyName("trim")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Trim { get; set; }

    /// <summary>
    /// A time range in seconds: prices whose <c>LastUpdateTime</c> is older than the most recent one by more
    /// than this are left out. Zero, the default, keeps every price.
    /// </summary>
    /// <remarks>
    /// rippled reads this parameter as <c>time_threshold</c>; the xrpl.org reference and xrpl.js name it
    /// <c>trim_threshold</c>, which the node ignores.
    /// </remarks>
    [JsonPropertyName("time_threshold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? TimeThreshold { get; set; }
}

/// <summary>
/// Identifies one <c>Oracle</c> object for a <see cref="GetAggregatePriceRequest"/>.
/// </summary>
public class OracleIdentifier
{
    /// <summary>
    /// Creates an empty identifier.
    /// </summary>
    public OracleIdentifier() { }

    /// <summary>
    /// Identifies the oracle <paramref name="oracleDocumentId"/> owned by <paramref name="account"/>.
    /// </summary>
    public OracleIdentifier(string account, uint oracleDocumentId)
    {
        Account = account;
        OracleDocumentId = oracleDocumentId;
    }

    /// <summary>
    /// The account that owns the <c>Oracle</c> object.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }

    /// <summary>
    /// The <c>OracleDocumentID</c> the owner gave the oracle.
    /// </summary>
    [JsonPropertyName("oracle_document_id")]
    public uint OracleDocumentId { get; set; }
}

/// <summary>
/// Response expected from a <see cref="GetAggregatePriceRequest"/>.
/// </summary>
public class GetAggregatePriceResponse : BaseMethodResult
{
    /// <summary>
    /// The statistics over every price collected.
    /// </summary>
    [JsonPropertyName("entire_set")]
    public AggregatePriceSet EntireSet { get; set; }

    /// <summary>
    /// The statistics over the prices left after trimming. Present only when the request set <see cref="GetAggregatePriceRequest.Trim"/>.
    /// </summary>
    [JsonPropertyName("trimmed_set")]
    public AggregatePriceSet TrimmedSet { get; set; }

    /// <summary>
    /// The median of the prices collected, as the decimal string the node returns.
    /// </summary>
    [JsonPropertyName("median")]
    public string Median { get; set; }

    /// <summary>
    /// The most recent <c>LastUpdateTime</c> among the prices collected (UTC). The node sends it in
    /// seconds since the Unix epoch, as the Oracle object holds it, not the Ripple epoch.
    /// </summary>
    [JsonPropertyName("time")]
    [JsonConverter(typeof(UnixDateTimeConverter))]
    public DateTime? Time { get; set; }

    /// <summary>
    /// The ledger index of the open ledger used, when the request read the current ledger.
    /// </summary>
    [JsonPropertyName("ledger_current_index")]
    public uint? LedgerCurrentIndex { get; set; }

    /// <summary>
    /// The ledger index of the closed ledger used, when the request read one.
    /// </summary>
    [JsonPropertyName("ledger_index")]
    public uint? LedgerIndex { get; set; }

    /// <summary>
    /// The hash of the closed ledger used, when the request read one.
    /// </summary>
    [JsonPropertyName("ledger_hash")]
    public string LedgerHash { get; set; }

    /// <summary>
    /// True if the data comes from a validated ledger version.
    /// </summary>
    [JsonPropertyName("validated")]
    public bool? Validated { get; set; }
}

/// <summary>
/// Price statistics over a set of oracle prices.
/// </summary>
public class AggregatePriceSet
{
    /// <summary>
    /// The simple mean, as the decimal string the node returns.
    /// </summary>
    [JsonPropertyName("mean")]
    public string Mean { get; set; }

    /// <summary>
    /// The number of prices the statistics cover.
    /// </summary>
    [JsonPropertyName("size")]
    public uint Size { get; set; }

    /// <summary>
    /// The sample standard deviation, as the decimal string the node returns; <c>0</c> for a single price.
    /// </summary>
    [JsonPropertyName("standard_deviation")]
    public string StandardDeviation { get; set; }
}
