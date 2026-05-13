using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Signed 32-bit integer type for XRPL serialization (type code 10).
    /// </summary>
    public class Int32Type : ISerializedType
    {
        public readonly int Value;

        public Int32Type(int value)
        {
            Value = value;
        }

        public void ToBytes(IBytesSink sink) => sink.Put(Bits.GetBytes(Value));

        public JsonNode ToJson() => JsonValue.Create(Value);

        public override string ToString() => Value.ToString();

        public static Int32Type FromJson(JsonNode token) => new Int32Type(token.GetValue<int>());

        public static Int32Type FromParser(BinaryParser parser, int? hint = null)
            => new Int32Type(Bits.ToInt32(parser.Read(4), 0));

        public static implicit operator Int32Type(int v) => new Int32Type(v);
        public static implicit operator int(Int32Type v) => v.Value;
    }
}
