using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/baseMethod.ts
//https://xrpl.org/error-formatting.html#error-formatting
namespace Xrpl.Models.Subscriptions;

public class ErrorResponse : BaseResponse
{
    /// <summary>
    /// A unique code for the type of error that occurred.
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; }

    /// <summary>
    /// (WebSocket only) The value success indicates the request was successfully received and understood by the server.<br/>
    /// Some client libraries omit this field on success.
    /// </summary>
    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("error_exception")]
    public string? ErrorException { get; set; }

    /// <summary>
    /// Where the <c>request</c> member sits inside the frame passed to
    /// <see cref="AttachFrame(byte[])"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not the parsed request: binding it to <see cref="object"/> made
    /// System.Text.Json build a <see cref="System.Text.Json.JsonElement"/> whose pooled backing
    /// array is never returned, on every error response - and <c>Sugar/Submit.cs</c> hits this
    /// branch on every poll of an unconfirmed transaction. Recording bounds costs nothing.
    /// </remarks>
    [JsonPropertyName("request")]
    [JsonConverter(typeof(JsonSliceConverter))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsonSlice RequestSlice { get; set; }

    /// <summary>
    /// A copy of the request that prompted this error, exactly as the node echoed it back.
    /// </summary>
    /// <remarks>
    /// Caution: if the original request carried secrets, they are echoed here — same warning as
    /// the field it replaces.
    /// </remarks>
    [JsonIgnore]
    public RawJson RawRequest =>
        _frame is null || RequestSlice.IsEmpty
            ? default
            : new RawJson(_frame, RequestSlice.Offset, RequestSlice.Length);

    /// <inheritdoc/>
    /// <remarks>
    /// Checks <see cref="RequestSlice"/> against the frame before deferring to
    /// <see cref="BaseResponse.AttachFrame(byte[])"/> for <c>result</c> and <c>id</c>, so a frame
    /// that does not fit any of the three recorded slices is rejected here rather than lazily,
    /// inside a consumer's read of <see cref="RawRequest"/>.
    /// </remarks>
    internal override void AttachFrame(byte[] frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        ValidateSliceFitsFrame(RequestSlice, frame);
        base.AttachFrame(frame);
    }
}