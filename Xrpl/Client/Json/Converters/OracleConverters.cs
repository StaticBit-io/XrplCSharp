using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Utils.Hashes;

namespace Xrpl.Client.Json.Converters
{
    internal static class OracleAsciiValidation
    {
        internal static void ValidatePrintableAsciiChars(ReadOnlySpan<char> chars, string context)
        {
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c < (char)0x20 || c > (char)0x7E)
                {
                    throw new JsonException(
                        $"{context} must use printable ASCII (U+0020–U+007E). Invalid character U+{((ushort)c):X4} at index {i}.");
                }
            }
        }

        internal static void ValidatePrintableAsciiBytes(ReadOnlySpan<byte> bytes, string context)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b < 0x20 || b > 0x7E)
                {
                    throw new JsonException(
                        $"{context} must decode to printable ASCII (0x20–0x7E). Invalid byte 0x{b:X2} at index {i}.");
                }
            }
        }
    }

    /// <summary>
    /// Converts AssetPrice values between numeric and hexadecimal string representation.
    /// When writing, converts ulong to lowercase hex string without leading zeros.
    /// When reading, accepts both hex strings and numeric values.
    /// </summary>
    public sealed class AssetPriceConverter : JsonConverter<object>
    {
        /// <summary>
        /// Reads an AssetPrice value from JSON, accepting both hex strings and integers.
        /// Returns <c>ulong</c> for numeric/hex input, <c>null</c> for null.
        /// </summary>
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                string hexStr = reader.GetString();
                try
                {
                    return Convert.ToUInt64(hexStr, 16);
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw new JsonException(
                        $"Invalid AssetPrice hex string '{hexStr}': {ex.Message}", ex);
                }
            }

            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetUInt64();

            throw new JsonException($"Invalid AssetPrice value");
        }

        /// <summary>
        /// Writes an AssetPrice value to JSON as a lowercase hexadecimal string.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            ulong numericValue = value switch
            {
                ulong ul => ul,
                uint u => u,
                long l when l >= 0 => (ulong)l,
                int i when i >= 0 => (ulong)i,
                _ => throw new JsonException($"Unsupported AssetPrice value: {value}")
            };

            writer.WriteStringValue(numericValue.ToString("x"));
        }
    }

    /// <summary>
    /// Converts Oracle currency identifiers (BaseAsset/QuoteAsset) following XRPL Currency rules:
    /// standard codes are exactly three characters as plain strings; nonstandard codes use 40-character hex
    /// (see <see href="https://xrpl.org/docs/references/protocol/data-types/currency-formats">Currency formats</see>).
    /// Serialization matches <see cref="Hashes.CurrencyToHex"/>.
    /// </summary>
    public sealed class OracleCurrencyConverter : JsonConverter<string>
    {
        /// <summary>
        /// Reads an Oracle currency from JSON, accepting both hex strings and plain currency codes.
        /// </summary>
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException(
                    $"Oracle currency must be a JSON string; got token type {reader.TokenType}.");
            }

            string value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return value;

            // Standard currency code (exactly three characters), per XRPL Currency type JSON display rules.
            if (value.Length == 3)
            {
                OracleAsciiValidation.ValidatePrintableAsciiChars(value.AsSpan(), "Oracle currency code");
                return value;
            }

            // 40-char hex string - decode to currency code
            if (value.Length == 40 && IsHexString(value))
            {
                return DecodeOracleCurrency(value);
            }

            OracleAsciiValidation.ValidatePrintableAsciiChars(value.AsSpan(), "Oracle currency code");
            if (value.Length > 20)
            {
                throw new JsonException("Oracle currency plain text must be at most 20 ASCII characters.");
            }

            return value;
        }

        /// <summary>
        /// Writes an Oracle currency using the same rules as <see cref="Hashes.CurrencyToHex"/>.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            OracleAsciiValidation.ValidatePrintableAsciiChars(value.AsSpan(), "Oracle currency code");

            string encoded;
            try
            {
                encoded = value.CurrencyToHex();
            }
            catch (XrplException ex)
            {
                throw new JsonException(ex.Message, ex);
            }

            if (encoded.Length == 40 && IsHexString(encoded))
                writer.WriteStringValue(encoded.ToLowerInvariant());
            else
                writer.WriteStringValue(encoded);
        }

        private static bool IsHexString(string value)
        {
            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string DecodeOracleCurrency(string hex)
        {
            byte[] bytes = new byte[20];
            for (int i = 0; i < 20; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            int length = 0;
            for (int i = 0; i < 20 && bytes[i] != 0; i++)
                length++;

            OracleAsciiValidation.ValidatePrintableAsciiBytes(bytes.AsSpan(0, length), "Oracle currency code");
            return Encoding.ASCII.GetString(bytes, 0, length);
        }
    }

    /// <summary>
    /// Converts Oracle string fields (Provider, AssetClass, URI) to/from hexadecimal ASCII representation.
    /// </summary>
    public sealed class OracleHexStringConverter : JsonConverter<string>
    {
        /// <summary>
        /// Reads an Oracle hex string from JSON, decoding from hex if necessary.
        /// </summary>
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException(
                    $"Oracle hex string field must be a JSON string; got token type {reader.TokenType}.");
            }

            string value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.Length % 2 == 0 && IsHexString(value))
                return DecodeHexString(value);

            OracleAsciiValidation.ValidatePrintableAsciiChars(value.AsSpan(), "Oracle hex string");
            return value;
        }

        /// <summary>
        /// Writes an Oracle string to JSON as hexadecimal ASCII.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            OracleAsciiValidation.ValidatePrintableAsciiChars(value.AsSpan(), "Oracle hex string");
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            writer.WriteStringValue(BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant());
        }

        private static bool IsHexString(string value)
        {
            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string DecodeHexString(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            OracleAsciiValidation.ValidatePrintableAsciiBytes(bytes, "Oracle hex string");
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
