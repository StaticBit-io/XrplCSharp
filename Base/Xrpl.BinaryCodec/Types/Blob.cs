using System;
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
        /// Defines how to read a Blob from json: the value must be an even-length hex string,
        /// an empty string being zero bytes.
        /// </summary>
        /// <remarks>
        /// A value that is not hex is refused rather than encoded as text, as rippled and xrpl.js
        /// refuse it. Encoding it would sign bytes other than the value the caller was given and
        /// showed, with no signal - and non-ASCII characters would become <c>?</c>. Text is
        /// encoded explicitly, with <see cref="FromAscii"/> or <see cref="FromString"/>.
        /// </remarks>
        /// <param name="token">json token</param>
        /// <returns>A Blob object</returns>
        /// <exception cref="FormatException">The value is not an even-length hex string.</exception>
        public static Blob FromJson(JsonNode token)
        {
            string str = token.GetValue<string>();
            if (!IsValidHexString(str))
            {
                throw new FormatException("A Blob value must be an even-length hex string.");
            }
            return FromHex(str);
        }

        /// <summary>
        /// Checks if a string is a valid hex-encoded string.
        /// </summary>
        /// <param name="str">The string to check.</param>
        /// <returns>True if the string is valid hex (even length, all hex chars); an empty string is.</returns>
        private static bool IsValidHexString(string str)
        {
            if (str == null || str.Length % 2 != 0)
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