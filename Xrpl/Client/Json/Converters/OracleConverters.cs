using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
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
                return Convert.ToUInt64(hexStr, 16);
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
    /// Converts Oracle currency identifiers (BaseAsset/QuoteAsset) following XRPL standard rules.
    /// Currencies with 3 characters or less remain as plain strings (XRP, USD, BTC).
    /// Currencies with more than 3 characters are converted to 40-character left-aligned hex.
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

            string value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return value;

            // Already a plain 3-char code
            if (value.Length <= 3)
                return value;

            // 40-char hex string - decode to currency code
            if (value.Length == 40 && IsHexString(value))
            {
                return DecodeOracleCurrency(value);
            }

            return value;
        }

        /// <summary>
        /// Writes an Oracle currency to JSON following XRPL standard rules.
        /// 3 characters or less = plain string. More than 3 characters = 40-char hex.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // Already a 40-char hex string - pass through
            if (value.Length == 40 && IsHexString(value))
            {
                writer.WriteStringValue(value);
                return;
            }

            // 3 characters or less - write as plain string (XRP, USD, BTC, etc.)
            if (value.Length <= 3)
            {
                writer.WriteStringValue(value);
                return;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length > 20)
                throw new JsonException($"Oracle currency codes must be 20 ASCII bytes or fewer, got {bytes.Length}.");
            byte[] paddedBytes = new byte[20];
            Array.Copy(bytes, 0, paddedBytes, 0, bytes.Length);
            writer.WriteStringValue(BitConverter.ToString(paddedBytes).Replace("-", "").ToLowerInvariant());
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

            string value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.Length % 2 == 0 && IsHexString(value))
            {
                try
                {
                    return DecodeHexString(value);
                }
                catch
                {
                    return value;
                }
            }

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
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
