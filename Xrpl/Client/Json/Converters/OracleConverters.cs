using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
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

        /// <summary>
        /// Whether every byte is printable ASCII, which is what a value the ledger holds as text
        /// looks like. A value that is not is still legitimate: rippled checks the length of
        /// Provider, AssetClass and URI and nothing else (OracleSet::preflight), so they are
        /// Blob fields carrying arbitrary bytes.
        /// </summary>
        internal static bool IsPrintableAscii(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] < 0x20 || bytes[i] > 0x7E)
                    return false;
            }
            return true;
        }

        internal static bool IsHexString(ReadOnlySpan<char> value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
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

            throw new JsonException($"Invalid AssetPrice value; expected String or Number token, got {reader.TokenType}.");
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
                return value;
            }

            // 40-char hex string - decode to currency code
            if (value.Length == 40 && OracleAsciiValidation.IsHexString(value.AsSpan()))
            {
                return DecodeOracleCurrency(value);
            }

            // Anything else is handed back as it arrived. A currency code is 160 bits the
            // ledger does not constrain, so what a third party published is not ours to refuse
            // on the way in; the write path still holds callers to a code it can encode.
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

            // UPPERCASE for 40-char codes: rippled STCurrency::to_json emits
            // nonstandard currency codes via strHex (uppercase)
            writer.WriteStringValue(encoded);
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

            // Everything after the text has to be padding for the text to be the whole value.
            // A code like 5553440001... would otherwise read as "USD" and lose the 0x01 when it
            // was written back, and a code of twenty zero bytes would read as an empty string.
            for (int i = length; i < 20; i++)
            {
                if (bytes[i] != 0)
                    return hex;
            }

            // Three characters of text written back are a standard code, which the ledger stores at
            // bytes 12-14; text at byte 0 is a different, nonstandard currency, so it stays hex.
            if (length == 3)
                return hex;

            return length > 0 && OracleAsciiValidation.IsPrintableAscii(bytes.AsSpan(0, length))
                ? Encoding.ASCII.GetString(bytes, 0, length)
                : hex;
        }
    }

    /// <summary>
    /// Writes the <c>base_asset</c> / <c>quote_asset</c> of a <c>get_aggregate_price</c> request in the form
    /// rippled displays the stored currency in (<c>to_string(Currency)</c>).
    /// </summary>
    /// <remarks>
    /// rippled finds a price by comparing the request string with the display form of the stored
    /// <c>PriceData.BaseAsset</c> / <c>QuoteAsset</c>, as text. Any other spelling of the same currency
    /// parses, yet matches nothing, and the request fails with <c>objectNotFound</c>. The display form is:
    /// <c>XRP</c> for the zero currency, the three characters for a code holding only an ISO-style code,
    /// and uppercase hex for anything else. A longer name is encoded as <see cref="OracleCurrencyConverter"/>
    /// encodes it for <c>OracleSet</c>, so the name written into an oracle finds that oracle's price.
    /// </remarks>
    public sealed class AggregatePriceAssetConverter : JsonConverter<string>
    {
        private const string IsoCharSet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<>(){}[]|?!@#$%^&*";

        private const int IsoCodeOffset = 12;
        private const int IsoCodeLength = 3;

        /// <summary>
        /// Reads the code as the node gives it.
        /// </summary>
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException(
                    $"An aggregate price asset must be a JSON string; got token type {reader.TokenType}.");
            }

            return reader.GetString();
        }

        /// <summary>
        /// Writes the code in rippled's display form.
        /// </summary>
        /// <exception cref="JsonException">The code is not printable ASCII or does not fit in 160 bits.</exception>
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(ToDisplayForm(value));
        }

        /// <summary>
        /// The form rippled displays <paramref name="code"/> in once it is stored as a currency.
        /// </summary>
        /// <exception cref="JsonException">The code is not printable ASCII or does not fit in 160 bits.</exception>
        public static string ToDisplayForm(string code)
        {
            OracleAsciiValidation.ValidatePrintableAsciiChars(code.AsSpan(), "Aggregate price asset");

            string trimmed = code.Trim();
            if (trimmed.Length == IsoCodeLength)
                return trimmed;

            string hex;
            if (trimmed.Length == 40 && OracleAsciiValidation.IsHexString(trimmed.AsSpan()))
            {
                hex = trimmed;
            }
            else
            {
                try
                {
                    hex = trimmed.CurrencyToHex();
                }
                catch (XrplException ex)
                {
                    throw new JsonException(ex.Message, ex);
                }
            }

            return DisplayFormOfHex(hex);
        }

        private static string DisplayFormOfHex(string hex)
        {
            byte[] bytes = new byte[20];
            for (int i = 0; i < 20; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            bool outsideIsoIsZero = true;
            for (int i = 0; i < 20; i++)
            {
                if ((i < IsoCodeOffset || i >= IsoCodeOffset + IsoCodeLength) && bytes[i] != 0)
                {
                    outsideIsoIsZero = false;
                    break;
                }
            }

            if (outsideIsoIsZero)
            {
                if (bytes[IsoCodeOffset] == 0 && bytes[IsoCodeOffset + 1] == 0 && bytes[IsoCodeOffset + 2] == 0)
                    return "XRP";

                string iso = Encoding.ASCII.GetString(bytes, IsoCodeOffset, IsoCodeLength);
                if (iso != "XRP" && IsIsoCode(iso))
                    return iso;
            }

            if (IsNoCurrency(bytes))
                return "1";

            return hex.ToUpperInvariant();
        }

        private static bool IsIsoCode(string iso)
        {
            for (int i = 0; i < iso.Length; i++)
            {
                if (IsoCharSet.IndexOf(iso[i]) < 0)
                    return false;
            }
            return true;
        }

        private static bool IsNoCurrency(byte[] bytes)
        {
            for (int i = 0; i < 19; i++)
            {
                if (bytes[i] != 0)
                    return false;
            }
            return bytes[19] == 1;
        }
    }

    /// <summary>
    /// Converts an <see cref="OracleBlob"/> (Oracle <c>Provider</c>, <c>AssetClass</c>, <c>URI</c>)
    /// to and from the hex string rippled uses for a Blob field.
    /// </summary>
    /// <remarks>
    /// Reading keeps the bytes whatever they are, so writing gives back exactly the bytes read.
    /// Reading is also total for anything the node sends: these fields are not constrained by
    /// rippled, and refusing one oracle's bytes threw out of the converter and took the whole
    /// response with it - one third-party oracle in a <c>ledger_data</c> page from devnet made
    /// the page unreadable. A value that is not hex is refused, as rippled refuses it, rather than
    /// taken as text: taking it as text is what signed bytes other than the ones received.
    /// </remarks>
    public sealed class OracleBlobConverter : JsonConverter<OracleBlob>
    {
        /// <summary>
        /// Reads the hex string of a Blob field.
        /// </summary>
        /// <exception cref="JsonException">The value is not a string, or not an even-length hex string.</exception>
        public override OracleBlob Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException(
                    $"An Oracle Blob field must be a JSON string; got token type {reader.TokenType}.");
            }

            string value = reader.GetString();
            try
            {
                return OracleBlob.FromHex(value);
            }
            catch (FormatException ex)
            {
                throw new JsonException(
                    "An Oracle Blob field must be an even-length hex string; text is encoded with OracleBlob.FromText.", ex);
            }
        }

        /// <summary>
        /// Writes the bytes as UPPERCASE hex, as rippled writes a Blob field (<c>strHex</c>).
        /// </summary>
        public override void Write(Utf8JsonWriter writer, OracleBlob value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.Hex);
        }
    }
}
