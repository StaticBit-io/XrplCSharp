using System;
using System.Collections.Generic;
using System.Text.Json;
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

        /// <summary>
        /// Members of this stream event that no declared property on the concrete subclass claims
        /// - rippled sends several fields unconditionally on every push of a given stream
        /// (<c>network_id</c> on <c>ledgerClosed</c>/the <c>ledger</c> stream's subscribe reply,
        /// <c>ctid</c> and the <c>account_history_*</c> trio on <c>transaction</c> events) that no
        /// model here declared a property for, and they vanished on the way to a caller. Declared
        /// on the shared base, mirroring <see cref="Ledger.BaseLedgerEntry.UnknownFields"/>,
        /// <see cref="Transactions.BaseTransactionResponse.UnknownFields"/> and
        /// <see cref="Methods.BaseMethodResult.UnknownFields"/> for their own families, so every
        /// stream type - <see cref="Methods.LedgerStream"/>, <see cref="Methods.ValidationStream"/>,
        /// <see cref="TransactionStream"/> - picks it up without repeating the attribute per class.
        /// </summary>
        /// <remarks>
        /// This is not a substitute for <see cref="Raw"/>: values here have already gone through
        /// JSON parsing (numbers, strings, nested objects as <see cref="JsonElement"/>), while
        /// <see cref="Raw"/> is the exact bytes the node sent. Use <see cref="Raw"/> when
        /// byte-for-byte fidelity matters; use this when a caller just needs to read a field the
        /// model does not yet declare - see <see cref="Methods.BaseMethodResult.UnknownFields"/>'s
        /// remarks for the retention cost of doing so.
        /// </remarks>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> UnknownFields { get; set; }
    }
}