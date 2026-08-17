using System.ComponentModel;
using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;

using Xrpl.Client.Json;
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
        /// Where the <c>result</c> member sits inside <see cref="Frame"/>.
        /// </summary>
        /// <remarks>
        /// Deliberately not the parsed result: binding it to <see cref="object"/> made
        /// System.Text.Json build a <see cref="System.Text.Json.JsonElement"/> whose pooled backing
        /// array is never returned, and the member was then parsed a second time to reach the
        /// requested type. Recording bounds costs nothing and leaves exactly one parse, cut
        /// straight from the frame.
        /// </remarks>
        [JsonPropertyName("result")]
        [JsonConverter(typeof(JsonSliceConverter))]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public JsonSlice ResultSlice { get; set; }

        [JsonIgnore]
        private byte[]? _frame;

        /// <summary>
        /// Pairs this envelope with the frame it was read from.
        /// </summary>
        /// <remarks>
        /// One call instead of a settable property, so the bounds are checked against the buffer
        /// once, where the two meet — a frame that does not match the recorded slice is rejected
        /// here rather than lazily, inside a consumer's read of <see cref="RawResult"/>. Internal
        /// on purpose: the bounds are only meaningful for a reader that covered one contiguous
        /// buffer, which the Stream overloads of System.Text.Json do not, and keeping this
        /// unreachable disarms that path by construction.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="frame"/> is too short for the recorded slice.
        /// </exception>
        internal void AttachFrame(byte[] frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (!ResultSlice.IsEmpty
                && (ResultSlice.Offset > frame.Length || ResultSlice.Length > frame.Length - ResultSlice.Offset))
            {
                throw new ArgumentException(
                    $"Frame of {frame.Length} bytes does not contain the recorded result at "
                    + $"[{ResultSlice.Offset}, {ResultSlice.Offset + (long)ResultSlice.Length}).",
                    nameof(frame));
            }

            _frame = frame;
        }

        /// <summary>
        /// The <c>result</c> member exactly as the node sent it.
        /// </summary>
        [JsonIgnore]
        public RawJson RawResult =>
            _frame is null || ResultSlice.IsEmpty
                ? default
                : new RawJson(_frame, ResultSlice.Offset, ResultSlice.Length);
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
