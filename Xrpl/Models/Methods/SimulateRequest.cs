using Newtonsoft.Json;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transactions;

namespace Xrpl.Models.Methods;

/// <summary>
/// The simulate method executes a dry run of any transaction type,
/// enabling you to preview the results and metadata of a transaction without committing them to the XRP Ledger.<br/>
/// Since this command never submits a transaction to the network, it doesn't incur any fees.<br/>
/// Expects a response in the form of a  <see cref="Submit"/> .
/// </summary>
public class SimulateRequest : BaseRequest
{
    public SimulateRequest()
    {
        Command = "simulate";
    }

    /// <summary>
    /// The transaction to simulate, in binary format.<br/>
    /// If you include this field, do not also include tx_json.
    /// </summary>
    [JsonProperty("tx_blob")]
    public string TxBlob { get; set; }

    /// <summary>
    /// The transaction to simulate, in JSON format.<br/>
    /// If you include this field, do not also include tx_blob.
    /// </summary>
    [JsonProperty("tx_json")]
    public ITransactionCommon Transaction { get; set; }

    /// <summary>
    /// The default value is false, which returns data and metadata in JSON format.<br/>
    /// If true, returns data and metadata in binary format, serialized to a hexadecimal string.
    /// </summary>
    [JsonProperty("binary")]
    public bool? Binary { get; set; }
}
public class SimulateResponse
{
    [JsonProperty("applied")]
    public bool Applied { get; set; }
    /// <summary>
    /// Text result code indicating the preliminary result of the transaction,  for example `tesSUCCESS`.
    /// </summary>
    [JsonProperty("engine_result")]
    public string EngineResult { get; set; }

    /// <summary>
    /// Numeric version of the result code.
    /// </summary>
    [JsonProperty("engine_result_code")]
    public int EngineResultCode { get; set; }

    /// <summary>
    /// Human-readable explanation of the transaction's preliminary result.
    /// </summary>
    [JsonProperty("engine_result_message")]
    public string EngineResultMessage { get; set; }

    /// <summary>
    /// The transaction to simulate, in binary format.<br/>
    /// If you include this field, do not also include tx_json.
    /// </summary>
    [JsonProperty("tx_blob")]
    public string TxBlob { get; set; }

    /// <summary>
    /// The transaction to simulate, in JSON format.<br/>
    /// If you include this field, do not also include tx_blob.
    /// </summary>
    [JsonProperty("tx_json")]
    [JsonConverter(typeof(TransactionRequestConverter))]
    public dynamic TxJson { get; set; }

    /// <summary>
    /// The default value is false, which returns data and metadata in JSON format.<br/>
    /// If true, returns data and metadata in binary format, serialized to a hexadecimal string.
    /// </summary>
    [JsonProperty("binary")]
    public bool? Binary { get; set; }

    [JsonProperty("meta")]
    public Meta Meta { get; set; }

    [JsonProperty("ledger_index")]
    public ulong? LedgerIndex { get; set; }
}