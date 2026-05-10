using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Signed 64-bit integer type for XRPL serialization (type code 11).
    /// JSON representation is a string to avoid precision loss.
    /// </summary>
    public class Int64Type : ISerializedType
    {
        public readonly long Value;

        public Int64Type(long value)
        {
            Value = value;
        }

        public void ToBytes(IBytesSink sink) => sink.Put(Bits.GetBytes(Value));

        public JsonNode ToJson() => JsonValue.Create(Value.ToString());

        public override string ToString() => Value.ToString();

        public static Int64Type FromJson(JsonNode token)
        {
            JsonValueKind kind = token.GetValueKind();
            if (kind == JsonValueKind.Number)
            {
                return new Int64Type(token.GetValue<long>());
            }
            if (kind == JsonValueKind.String)
            {
                return new Int64Type(long.Parse(token.GetValue<string>()));
            }
            throw new FormatException($"Cannot parse Int64 from JSON kind {kind}");
        }

        public static Int64Type FromParser(BinaryParser parser, int? hint = null)
            => new Int64Type(Bits.ToInt64(parser.Read(8), 0));

        public static implicit operator Int64Type(long v) => new Int64Type(v);
        public static implicit operator long(Int64Type v) => v.Value;
    }
}
