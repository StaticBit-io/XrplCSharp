using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transactions;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/baseMethod.ts
//https://xrpl.org/response-formatting.html

namespace Xrpl.Models.Subscriptions
{
    public class BaseResponse
    {
        /// <summary>
        /// (WebSocket only) ID provided in the request that prompted this response
        /// </summary>
        [JsonPropertyName("id")]
        public object? Id { get; set; }

        /// <summary>
        /// "error" if the request caused an error
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }
        /// <summary>
        /// (WebSocket only) The value response indicates a direct response to an API request.<br/>
        /// Asynchronous notifications use a different value such as ledgerClosed or transaction.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }
        /// <summary>
        /// (WebSocket only) The value success indicates the request was successfully received and understood by the server.<br/>
        /// Some client libraries omit this field on success.
        /// </summary>
        [JsonPropertyName("result")]
        public object Result { get; set; }
        /// <summary>
        /// (May be omitted) If this field is provided, the value is the string load.<br/>
        /// This means the client is approaching the rate limiting threshold where the server will disconnect this client.
        /// </summary>
        [JsonPropertyName("warning")]
        public string Warning { get; set; }
        /// <summary>
        /// May be omitted) If this field is provided, it contains one or more Warnings Objects with important warnings.<br/>
        /// For details, see API Warnings (https://xrpl.org/response-formatting.html#api-warnings)
        /// </summary>
        [JsonPropertyName("warnings")]
        public List<RippleResponseWarning>? Warnings { get; set; }
        /// <summary>
        /// (May be omitted) If true, this request and response have been forwarded from a Reporting Mode
        /// server to a P2P Mode server (and back) because the request requires data that is not available in Reporting Mode.<br/>
        /// The default is false.
        /// </summary>
        [JsonPropertyName("forwarded")]
        public bool? Forwarded { get; set; }
        /// <summary>
        /// (May be omitted) The api_version specified in the request, if any.
        /// </summary>
        [JsonPropertyName("api_version")]
        public uint? ApiVersion { get; set; }
    }
    /// <summary>
    /// When the response contains a warnings array, each member of the array represents a separate warning from the server.
    /// </summary>
    public class RippleResponseWarning //todo rename to Waning
    {
        /// <summary>
        /// A unique numeric code for this warning message.
        /// </summary>
        [JsonPropertyName("id")]
        public uint Id { get; set; }
        /// <summary>
        /// A human-readable string describing the cause of this message.<br/>
        /// Do not write software that relies the contents of this message;<br/>
        /// use the id (and details, if applicable) to identify the warning instead.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }
        /// <summary>
        /// (May be omitted) Additional information about this warning.<br/>
        /// The contents vary depending on the type of warning.
        /// </summary>
        [JsonPropertyName("details")]
        public Dictionary<string, string>? Details { get; set; }
    }
}
