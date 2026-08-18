using System;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;

namespace Xrpl.Models.Subscriptions
{
    public class BaseStream
    {
        // Not [JsonIgnore]: System.Text.Json never serializes private fields, so the attribute
        // would be a no-op that misleads a reader into thinking it is load-bearing here.
        // Internal, not private: TransactionStream.RawTransaction reads this directly to build its
        // own RawJson window over the tx_json/transaction slice, the same way Raw does below.
        internal byte[]? _frame;

        private JsonSlice _documentSlice;

        /// <summary>
        /// consensusPhase indicates this is from the consensus stream<br/>
        /// consensusPhase - type
        /// </summary>
        /// <remarks>
        /// Nullable because absence is meaningful: an event built by hand rather than deserialized
        /// off the wire never had a <c>type</c> member to read. A non-nullable enum defaults to
        /// <see cref="ResponseStreamType.UNKNOWN"/> (0), which <c>JsonSerializer.Serialize</c> then
        /// wrote back out as the literal member <c>"type":"UNKNOWN"</c> - a value the node never
        /// sent, fabricated purely because the CLR type could not represent "no type at all".
        /// </remarks>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ResponseStreamType? Type { get; set; }

        /// <summary>
        /// This event exactly as the node sent it.
        /// </summary>
        /// <remarks>
        /// Unlike a query response, a stream message carries no <c>result</c> envelope to slice a
        /// member out of — the frame passed to <see cref="AttachFrame(byte[])"/> already is the
        /// event, so this spans the whole of it via <see cref="JsonSlice.OfDocument(byte[])"/>.
        /// Empty when the event was never paired with a frame (built by hand, or deserialized
        /// through a bare <c>JsonSerializer.Deserialize</c> call rather than the stream pipeline).
        /// </remarks>
        [JsonIgnore]
        public RawJson Raw =>
            _frame is null || _documentSlice.IsEmpty
                ? default
                : new RawJson(_frame, _documentSlice.Offset, _documentSlice.Length);

        /// <summary>
        /// Pairs this event with the frame it was read from.
        /// </summary>
        /// <remarks>
        /// Virtual so <see cref="TransactionStream"/> can compute its own tx_json/transaction
        /// slice from the same frame before deferring here. Unlike
        /// <see cref="ErrorResponse.AttachFrame(byte[])"/>, which validates a slice that arrived
        /// through deserialization before deferring to <see cref="BaseResponse.AttachFrame(byte[])"/>,
        /// there is nothing to validate on this path: <see cref="TransactionStream"/> derives its
        /// slice by scanning this same frame directly, via
        /// <see cref="Xrpl.Client.Json.JsonSlice.FindTopLevelMember"/>, so slice and frame cannot
        /// disagree.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
        internal virtual void AttachFrame(byte[] frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            _documentSlice = JsonSlice.OfDocument(frame);
            _frame = frame;
        }
    }
}