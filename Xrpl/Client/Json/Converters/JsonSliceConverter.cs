using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Records where a member sits in the frame instead of materializing it.
    /// </summary>
    /// <remarks>
    /// Deserializing <c>result</c> into <see cref="object"/> made System.Text.Json build a
    /// self-contained <see cref="JsonElement"/> for it, and <c>JsonDocument.ParseValue</c> rents
    /// the backing array from <see cref="System.Buffers.ArrayPool{T}"/> without ever returning it —
    /// 65 536 bytes for a 36 691-byte response, held for a subtree that was then parsed a second
    /// time to reach the requested type. Skipping the subtree and remembering its bounds costs
    /// nothing and leaves the single parse to the caller, straight out of the frame.
    /// </remarks>
    public sealed class JsonSliceConverter : JsonConverter<JsonSlice>
    {
        /// <inheritdoc />
        public override JsonSlice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            long start = reader.TokenStartIndex;
            reader.Skip();
            long end = reader.BytesConsumed;
            return new JsonSlice(checked((int)start), checked((int)(end - start)));
        }

        /// <summary>
        /// Always throws. A response envelope describes what a node sent; re-emitting it from the
        /// parsed form would produce a plausible but different document, which is the failure mode
        /// this type exists to remove.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, JsonSlice value, JsonSerializerOptions options)
        {
            throw new NotSupportedException(
                "A response envelope is not serializable: write the original bytes through RawJson instead.");
        }
    }
}
