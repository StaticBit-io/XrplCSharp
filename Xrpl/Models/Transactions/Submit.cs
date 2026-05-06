using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;

using JsonSerializer = System.Text.Json.JsonSerializer;

//https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/methods/submit.ts#L28
namespace Xrpl.Models.Transactions;

/// <summary>
/// Response expected from a  <see cref="SubmitRequest"/>.
/// </summary>
public class Submit //todo rename to SubmitResponse extends BaseResponse
{
    [JsonPropertyName("Accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("applied")]
    public bool Applied { get; set; }

    [JsonPropertyName("broadcast")]
    public bool Broadcast { get; set; }

    [JsonPropertyName("open_ledger_cost")]
    public string OpenLedgerCost { get; set; }

    /// <summary>
    /// Text result code indicating the preliminary result of the transaction,  for example `tesSUCCESS`.
    /// </summary>
    [JsonPropertyName("engine_result")]
    public string EngineResult { get; set; }

    /// <summary>
    /// Numeric version of the result code.
    /// </summary>
    [JsonPropertyName("engine_result_code")]
    public int EngineResultCode { get; set; }

    /// <summary>
    /// Human-readable explanation of the transaction's preliminary result.
    /// </summary>
    [JsonPropertyName("engine_result_message")]
    public string EngineResultMessage { get; set; }

    /// <summary>
    /// The complete transaction in hex string format.
    /// </summary>
    [JsonPropertyName("tx_blob")]
    public string TxBlob { get; set; }

    /// <summary>
    /// Next account sequence number.
    /// </summary>
    [JsonPropertyName("account_sequence_next")]
    public uint? AccountSequenceNext { get; set; }

    /// <summary>
    /// Available account sequence number.
    /// </summary>
    [JsonPropertyName("account_sequence_available")]
    public uint? AccountSequenceAvailable { get; set; }

    /// <summary>
    /// The complete transaction in JSON format.
    /// </summary>
    [JsonPropertyName("tx_json")]
    public object TxJson { get; set; }

    //[JsonIgnore]
    /// <summary>
    /// The complete transaction.
    /// </summary>
    public ITransactionResponse Transaction => JsonSerializer.Deserialize<TransactionResponse>(TxJson.ToString(), XrplJsonOptions.Default);


    //todo not found fields accepted: boolean,  account_sequence_available: number, account_sequence_next: number, applied: boolean,  broadcast: boolean
    //kept: boolean,  queued: boolean, open_ledger_cost: string,  validated_ledger_index: number
}