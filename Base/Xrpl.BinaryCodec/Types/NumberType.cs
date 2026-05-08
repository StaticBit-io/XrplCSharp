using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// XLS-66 Number type (type code 9). Serialized as 8 bytes big-endian.
    /// Used for Oracle PriceData fields (AssetPrice, etc.).
    /// JSON representation is a numeric value or string.
    /// </summary>
    public class NumberType : ISerializedType
    {
        public readonly ulong RawValue;

        public NumberType(ulong rawValue)
        {
            RawValue = rawValue;
        }

        public void ToBytes(IBytesSink sink) => sink.Put(Bits.GetBytes(RawValue));

        public JsonNode ToJson() => JsonValue.Create(RawValue.ToString());

        public override string ToString() => RawValue.ToString();

        public static NumberType FromJson(JsonNode token)
        {
            JsonValueKind kind = token.GetValueKind();
            if (kind == JsonValueKind.Number)
            {
                return new NumberType(token.GetValue<ulong>());
            }
            if (kind == JsonValueKind.String)
            {
                string str = token.GetValue<string>();
                return new NumberType(ulong.Parse(str));
            }
            throw new FormatException($"Cannot parse Number from JSON kind {kind}");
        }

        public static NumberType FromParser(BinaryParser parser, int? hint = null)
            => new NumberType(Bits.ToUInt64(parser.Read(8), 0));
    }
}
