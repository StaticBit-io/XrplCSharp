using Newtonsoft.Json;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/baseMethod.ts
//https://xrpl.org/error-formatting.html#error-formatting
namespace Xrpl.Models.Subscriptions;

public class ErrorResponse : BaseResponse
{
    /// <summary>
    /// A unique code for the type of error that occurred.
    /// </summary>
    [JsonProperty("error")]
    public string Error { get; set; }

    /// <summary>
    /// (WebSocket only) The value success indicates the request was successfully received and understood by the server.<br/>
    /// Some client libraries omit this field on success.
    /// </summary>
    [JsonProperty("error_message")]
    public string ErrorMessage { get; set; }

    [JsonProperty("error_code")]
    public string? ErrorCode { get; set; }

    [JsonProperty("error_exception")]
    public string? ErrorException { get; set; }

    /// <summary>
    /// A copy of the request that prompted this error, in JSON format.<br/>
    /// Caution: If the request contained any secrets, they are copied here!
    /// </summary>
    [JsonProperty("request")]

    public object Request { get; set; }
}