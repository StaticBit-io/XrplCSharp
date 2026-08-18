using System;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;

namespace Xrpl.Models.Subscriptions
{
    public class BaseStream
    {
        // Not [JsonIgnore]: System.Text.Json never serializes private fields, so the attribute
        // would be a no-op that misleads a reader into thinking it is load-bearing here.
        // Internal, not private: TransactionStream records its own slice (tx_json/transaction)
        // over the same frame and validates it against this the same way AttachFrame does below.
        internal byte[]? _frame;

        private JsonSlice _documentSlice;

        /// <summary>
        /// consensusPhase indicates this is from the consensus stream<br/>
        /// consensusPhase - type
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ResponseStreamType Type { get; set; }

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
        /// Virtual so <see cref="TransactionStream"/> can validate its own tx_json/transaction
        /// slice against the same frame before deferring here, matching how
        /// <see cref="ErrorResponse.AttachFrame(byte[])"/> checks its own slice before deferring to
        /// <see cref="BaseResponse.AttachFrame(byte[])"/>.
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