using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using System.Text.RegularExpressions;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

// https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-binary-codec/src/types/uint-64.ts


namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Derived UInt class for serializing/deserializing 64 bit UInt
    /// </summary>
    public class Uint64 : Uint<ulong>
    {
        static string HEX_REGEX = @"^[a-fA-F0-9]{1,16}$";
        static string DECIMAL_REGEX = @"^[0-9]+$";

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="value">ulong value</param>
        public Uint64(ulong value) : base(value)
        {
        }

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="value">byte value</param>
        public Uint64(byte value) : base(value)
        {
        }

        /// <inheritdoc />
        public override byte[] ToBytes() => Bits.GetBytes(Value);

        /// <inheritdoc />
        public override string ToString() => B16.Encode(ToBytes());

        /// <summary> Deserialize Uint64 from JSON </summary>
        /// <param name="token">json token - supports both decimal strings (e.g., "1234567890") and hex strings (e.g., "0x499602D2" or "499602D2")</param>
        /// <returns>Uint64 value</returns>
        public static Uint64 FromJson(JsonNode token)
        {
            if (token is JsonValue jv && jv.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            {
                return new Uint64(jv.GetValue<ulong>());
            }

            string str = token.GetValue<string>();
            
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hexPart = str.Substring(2).PadLeft(16, '0');
                return new Uint64(Bits.ToUInt64(B16.Decode(hexPart), 0));
            }
            
            if (Regex.IsMatch(str, DECIMAL_REGEX))
            {
                return new Uint64(ulong.Parse(str));
            }
            
            if (Regex.IsMatch(str, HEX_REGEX))
            {
                string padded = str.PadLeft(16, '0');
                return new Uint64(Bits.ToUInt64(B16.Decode(padded), 0));
            }
            
            throw new FormatException($"Cannot parse '{str}' as Uint64. Expected decimal or hex string.");
        }

        public static implicit operator Uint64(ulong v) => new Uint64(v);

        /// <summary>
        /// create instance of this value
        /// </summary>
        /// <param name="v">byte value</param>
        public static implicit operator Uint64(byte v) => new Uint64(v);

        /// <summary>
        /// create instance of this value from string
        /// </summary>
        public static Uint64 FromValue(int v)
        {
            byte[] valueBytes = Bits.GetBytes(v);
            return new Uint64(Bits.ToUInt32(valueBytes, 0));
        }

        /// <summary>
        /// create instance of this value from hex string
        /// </summary>
        public static Uint64 FromValue(string v)
        {
            Regex rg = new Regex(HEX_REGEX);
            if (rg.Matches(v).Count == 0)
            {
                throw new BinaryCodecException($"{v} is not a valid hex string");
            }

            string strBuf = v.PadRight(16, '0');
            return new Uint64(Bits.ToUInt64(strBuf.FromHex(), 0));
        }

        /// <inheritdoc />
        public override JsonNode ToJson()
        {
            return JsonValue.Create(ToBytes().ToHex());
        }

        /// <summary>
        /// Construct a Uint64 from a BinaryParser
        /// </summary>
        /// <param name="parser">A BinaryParser to read Uint64 from</param>
        /// <returns></returns>
        public static Uint64 FromParser(BinaryParser parser, int? hint=null) => Bits.ToUInt64(parser.Read(8), 0);
    }
}