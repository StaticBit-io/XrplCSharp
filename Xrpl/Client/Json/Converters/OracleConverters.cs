using System;
using System.Text;
using Newtonsoft.Json;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Converts AssetPrice values between numeric and hexadecimal string representation.
    /// When writing, converts ulong to lowercase hex string without leading zeros.
    /// When reading, accepts both hex strings and numeric values.
    /// </summary>
    public sealed class AssetPriceConverter : JsonConverter
    {
        /// <summary>
        /// Determines whether this converter can convert the specified object type.
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return true;
        }

        /// <summary>
        /// Reads an AssetPrice value from JSON, accepting both hex strings and integers.
        /// </summary>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType == JsonToken.String)
            {
                var hexStr = (string)reader.Value;
                return Convert.ToUInt64(hexStr, 16);
            }

            if (reader.TokenType == JsonToken.Integer)
                return Convert.ToUInt64(reader.Value);

            throw new JsonSerializationException($"Invalid AssetPrice value: {reader.Value}");
        }

        /// <summary>
        /// Writes an AssetPrice value to JSON as a lowercase hexadecimal string.
        /// </summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var numValue = Convert.ToUInt64(value);
            writer.WriteValue(numValue.ToString("x"));
        }
    }

    /// <summary>
    /// Converts Oracle currency identifiers (BaseAsset/QuoteAsset) following XRPL standard rules.
    /// Currencies with 3 characters or less remain as plain strings (XRP, USD, BTC).
    /// Currencies with more than 3 characters are converted to 40-character left-aligned hex.
    /// </summary>
    public sealed class OracleCurrencyConverter : JsonConverter
    {
        /// <summary>
        /// Determines whether this converter can convert the specified object type.
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string);
        }

        /// <summary>
        /// Reads an Oracle currency from JSON, accepting both hex strings and plain currency codes.
        /// </summary>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var value = (string)reader.Value;
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
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var strValue = (string)value;

            // Already a 40-char hex string - pass through
            if (strValue.Length == 40 && IsHexString(strValue))
            {
                writer.WriteValue(strValue);
                return;
            }

            // 3 characters or less - write as plain string (XRP, USD, BTC, etc.)
            if (strValue.Length <= 3)
            {
                writer.WriteValue(strValue);
                return;
            }

            // More than 3 characters - convert to 40-char left-aligned hex (lowercase)
            var bytes = Encoding.ASCII.GetBytes(strValue);
            var paddedBytes = new byte[20];
            Array.Copy(bytes, 0, paddedBytes, 0, Math.Min(bytes.Length, 20));
            writer.WriteValue(BitConverter.ToString(paddedBytes).Replace("-", "").ToLowerInvariant());
        }

        private static bool IsHexString(string value)
        {
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string DecodeOracleCurrency(string hex)
        {
            var bytes = new byte[20];
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
    public sealed class OracleHexStringConverter : JsonConverter
    {
        /// <summary>
        /// Determines whether this converter can convert the specified object type.
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string);
        }

        /// <summary>
        /// Reads an Oracle hex string from JSON, decoding from hex if necessary.
        /// </summary>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var value = (string)reader.Value;
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
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var strValue = (string)value;

            if (strValue.Length % 2 == 0 && IsHexString(strValue))
            {
                writer.WriteValue(strValue);
                return;
            }

            var bytes = Encoding.ASCII.GetBytes(strValue);
            writer.WriteValue(BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant());
        }

        private static bool IsHexString(string value)
        {
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string DecodeHexString(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
