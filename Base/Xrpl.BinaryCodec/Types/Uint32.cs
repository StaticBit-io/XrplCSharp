using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/types/uint-32.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Derived UInt class for serializing/deserializing 32 bit UInt
    /// </summary>
    public class Uint32 : Uint<uint>
    {
        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="value">uint value</param>
        public Uint32(uint value) : base(value)
        {
        }

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="value">byte value</param>
        public Uint32(byte value) : base(value)
        {
        }

        /// <summary> Deserialize Uint32 </summary>
        /// <param name="token">json token</param>
        /// <returns>Uint32 value</returns>
        public static Uint32 FromJson(JsonNode token)
        {
            if (token.GetValueKind() == JsonValueKind.Number)
            {
                // A JsonValue built in code keeps its CLR type (int, uint, long, ...) and only
                // answers TryGetValue for exactly that type, so probe each integral type.
                JsonValue number = token.AsValue();
                if (number.TryGetValue(out uint exact))
                    return exact;

                decimal val;
                if (number.TryGetValue(out long l)) val = l;
                else if (number.TryGetValue(out int i)) val = i;
                else if (number.TryGetValue(out ulong ul)) val = ul;
                else if (number.TryGetValue(out ushort us)) val = us;
                else if (number.TryGetValue(out short s)) val = s;
                else if (number.TryGetValue(out byte b)) val = b;
                else if (number.TryGetValue(out sbyte sb)) val = sb;
                else if (number.TryGetValue(out JsonElement je))
                {
                    // Parsed JSON: "7", and also "7.0" or "7e0", which xrpl.js accepts as 7.
                    if (je.TryGetUInt32(out uint fromElement))
                        return fromElement;
                    if (!je.TryGetDecimal(out val))
                        throw new OverflowException($"Value {je.GetRawText()} is out of range for Uint32 (0..{uint.MaxValue}).");
                }
                else if (number.TryGetValue(out double dbl)) return FromIntegralDouble(dbl);
                else if (number.TryGetValue(out float flt)) return FromIntegralDouble(flt);
                else if (number.TryGetValue(out decimal dec)) val = dec;
                else throw new FormatException($"Cannot read '{token.ToJsonString()}' as Uint32.");

                if (val != Math.Truncate(val) || val < 0 || val > uint.MaxValue)
                    throw new OverflowException(
                        $"Value {val} is out of range for Uint32 (0..{uint.MaxValue}).");
                return (uint)val;
            }
            return uint.Parse(token.GetValue<string>(), CultureInfo.InvariantCulture);
        }

        // Checked as a double: converting to decimal first rounds 1.0000000000000002 to 1.
        private static Uint32 FromIntegralDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Floor(value) || value < 0 || value > uint.MaxValue)
                throw new OverflowException($"Value {value.ToString("R", CultureInfo.InvariantCulture)} is out of range for Uint32 (0..{uint.MaxValue}).");
            return (uint)value;
        }

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="v">uint value</param>
        public static implicit operator Uint32(uint v) => new Uint32(v);

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="v">byte value</param>
        public static implicit operator Uint32(byte v) => new Uint32(v);

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="v">uint value</param>
        public static implicit operator uint(Uint32 v) => v.Value;

        /// <inheritdoc />
        public override byte[] ToBytes() => Bits.GetBytes(Value);

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="v">int value</param>
        /// <exception cref="BinaryCodecException"><paramref name="v"/> is negative</exception>
        public static Uint32 FromValue(int v)
        {
            if (v < 0)
            {
                throw new BinaryCodecException($"Invalid UInt32: {v} must be >= 0");
            }
            return new Uint32((uint)v);
        }

        /// <summary>
        /// Construct a Uint16 from a BinaryParser
        /// </summary>
        /// <param name="parser">A BinaryParser to read Uint16 from</param>
        /// <returns></returns>
        public static Uint32 FromParser(BinaryParser parser, int? hint=null) => Bits.ToUInt32(parser.Read(4), 0);
    }
}