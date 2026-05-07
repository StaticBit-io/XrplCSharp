using System;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Represents a 192-bit (24-byte) hash (e.g., MPTokenIssuanceID).
    /// </summary>
    public class Hash192 : Hash
    {
        public const int Width = 24;

        public static readonly Hash192 Zero = new Hash192(new byte[24]);

        /// <inheritdoc />
        public Hash192(byte[] buffer) : base(buffer)
        {
            if (buffer == null || buffer.Length != 24)
                throw new ArgumentException("Hash192 buffer must be exactly 24 bytes", nameof(buffer));
        }

        /// <summary>Create instance from json object</summary>
        /// <param name="token">json object</param>
        public static Hash192 FromJson(JsonNode token) => FromHex(token.GetValue<string>());

        /// <summary>Create instance from hex string</summary>
        /// <param name="token">string hex token</param>
        public static Hash192 FromHex(string token) => new Hash192(B16.Decode(token));

        /// <summary>Create instance from binary parser</summary>
        /// <param name="parser">parser</param>
        /// <param name="hint"></param>
        public static Hash192 FromParser(BinaryParser parser, int? hint = null) => new Hash192(parser.Read(24));
    }
}