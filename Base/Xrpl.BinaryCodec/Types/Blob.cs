using System.Diagnostics;

using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

//https://xrpl.org/serialization.html#blob-fields
//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-binary-codec/src/types/blob.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// The Blob type is a length-prefixed field with arbitrary data.<br/>
    /// Two common fields that use this type are SigningPubKey and TxnSignature, which contain (respectively)
    /// the public key and signature that authorize a transaction to be executed.<br/>
    /// Blob fields have no further structure to their contents, so they consist of
    /// exactly the amount of bytes indicated in the variable-length encoding, after the Field ID and length prefixes.
    ///<br/><br/>
    /// Variable length encoded type
    /// </summary>
    public class Blob : ISerializedType
    {
        public readonly byte[] Buffer;
        private Blob(byte[] decode)
        {
            this.Buffer = decode;
        }
        public static Blob FromHex(string value)
        {
            return B16.Decode(value);
        }
        public static implicit operator Blob(byte[] value)
        {
            return new Blob(value);
        }
        public static implicit operator Blob(JsonNode token)
        {
            return FromJson(token);
        }
        /// <summary>
        /// Defines how to read a Blob from json.
        /// Accepts both hex-encoded strings and ASCII strings.
        /// If the string is valid hex (even length, all hex chars), it's decoded as hex.
        /// Otherwise, it's treated as ASCII and converted to bytes.
        /// </summary>
        /// <param name="token">json token</param>
        /// <returns>A Blob object</returns>
        public static Blob FromJson(JsonNode token)
        {
            var str = token.GetValue<string>();
            if (IsValidHexString(str))
            {
                return FromHex(str);
            }
            // Treat as ASCII string
            return FromAscii(str);
        }

        /// <summary>
        /// Checks if a string is a valid hex-encoded string.
        /// </summary>
        /// <param name="str">The string to check.</param>
        /// <returns>True if the string is valid hex (even length, all hex chars).</returns>
        private static bool IsValidHexString(string str)
        {
            if (string.IsNullOrEmpty(str) || str.Length % 2 != 0)
            {
                return false;
            }
            foreach (char c in str)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc />
        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Buffer);
        }

        /// <inheritdoc />
        public JsonNode ToJson() => JsonValue.Create(ToString());

        /// <inheritdoc />
        public override string ToString()
        {
            return B16.Encode(Buffer);
        }
        /// <summary>
        /// Defines how to read a Blob from a BinaryParser
        /// </summary>
        /// <param name="parser">The binary parser to read the Blob from</param>
        /// <param name="hint">The length of the blob, computed by readVariableLengthLength() and passed in</param>
        /// <returns>A Blob object</returns>
        public static Blob FromParser(BinaryParser parser, int? hint = null)
        {
            Debug.Assert(hint != null, "hint != null");
            return parser.Read((int)hint);
        }
        /// <summary>
        /// Create a Blob object from a hex-string
        /// </summary>
        /// <param name="blob">existing hex-string</param>
        /// <param name="encoding">string encoding</param>
        /// <returns>A Blob object</returns>
        public static Blob FromString(string blob, System.Text.Encoding encoding) => new Blob(encoding.GetBytes(blob));

        /// <summary>
        /// Create a Blob object from a hex-string
        /// </summary>
        /// <param name="blob">existing hex-string in ASCII</param>
        /// <returns>A Blob object</returns>
        public static Blob FromAscii(string blob) => FromString(blob, System.Text.Encoding.ASCII);
    }
}