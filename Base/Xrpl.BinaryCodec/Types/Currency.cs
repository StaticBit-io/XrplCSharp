using System;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-binary-codec/src/types/currency.ts
//https://xrpl.org/currency-formats.html#currency-formats

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Class defining how to encode and decode Currencies
    /// </summary>
    public class Currency : Hash160
    {
        /// <summary>
        ///  ISO code of this currency
        /// </summary>
        public readonly string IsoCode;
        /// <summary>
        /// Test if this amount is in units of Native Currency(XRP)
        /// </summary>
        public readonly bool IsNative;
        /// <summary>
        /// Native XRP Currency
        /// </summary>
        public static readonly Currency Xrp = new Currency(new byte[20]);
        /// <summary>
        /// Constructs a Currency object
        /// </summary>
        /// <param name="buffer">bytes buffer</param>
        public Currency(byte[] buffer) : base(buffer)
        {
            IsoCode = GetCurrencyCodeFromTlcBytes(buffer, out IsNative);
        }
        /// <summary>
        /// get currency code from bytes. Supports both standard XRPL format (bytes 12-14)
        /// and Oracle XLS-47 format (left-aligned bytes 0-2).
        /// </summary>
        /// <param name="bytes">bytes</param>
        /// <param name="isNative">will true if currency is XRP</param>
        /// <returns></returns>
        public static string GetCurrencyCodeFromTlcBytes(byte[] bytes, out bool isNative)
        {
            int i;
            var zeroInNonCurrencyBytes = true;
            var allZero = true;

            for (i = 0; i < 20; i++)
            {
                allZero = allZero && bytes[i] == 0;
                zeroInNonCurrencyBytes = zeroInNonCurrencyBytes && 
                    (i == 12 || i == 13 || i == 14 || bytes[i] == 0); 
            }
            if (allZero)
            {
                isNative = true;
                return "XRP";
            }
            // Standard XRPL format: currency code at bytes 12-14
            if (zeroInNonCurrencyBytes)
            {
                isNative = false;
                return IsoCodeFromBytesAndOffset(bytes, 12);
            }
            // Oracle XLS-47 format: left-aligned currency code at bytes 0-2
            // Check if bytes 3-19 are all zero and bytes 0-2 are valid ASCII
            bool isOracleFormat = true;
            for (i = 3; i < 20; i++)
            {
                if (bytes[i] != 0)
                {
                    isOracleFormat = false;
                    break;
                }
            }
            if (isOracleFormat && bytes[0] >= 0x20 && bytes[0] <= 0x7E)
            {
                isNative = false;
                return IsoCodeFromBytesAndOffset(bytes, 0);
            }
            isNative = false;
            return null;
        }

        private static char CharFrom(byte[] bytes, int i)
        {
            return (char)bytes[i];
        }
        /// <summary>
        /// Return the ISO code of this currency
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static string IsoCodeFromBytesAndOffset(byte[] bytes, int offset)
        {
            var a = CharFrom(bytes, offset);
            var b = CharFrom(bytes, offset + 1);
            var c = CharFrom(bytes, offset + 2);
            return "" + a + b + c;
        }
        /// <summary>
        /// decode currency from json field
        /// </summary>
        /// <param name="token">json field</param>
        /// <returns></returns>
        public new static Currency FromJson(JsonNode token)
        {
            return token == null ? null : FromString(token.GetValue<string>());
        }
        /// <summary>
        /// Decode currency from JSON for Oracle fields (XLS-47 format).
        /// Uses left-aligned ASCII encoding for 3-letter codes.
        /// </summary>
        /// <param name="token">JSON token containing currency code.</param>
        /// <returns>Currency with Oracle encoding.</returns>
        public static Currency FromOracleJson(JsonNode token)
        {
            return token == null ? null : FromOracleString(token.GetValue<string>());
        }
        /// <summary>
        /// decode currency from string
        /// </summary>
        /// <param name="str">string currency code</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static Currency FromString(string str)
        {
            if (str == "XRP")
            {
                return Xrp;
            }
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (str.Length)
            {
                case 40:
                    return new Currency(B16.Decode(str));
                case 3:
                    // Standard XRPL currency format: code at bytes 12-14
                    return new Currency(EncodeCurrency(str));
            }
            throw new InvalidOperationException(
                "Currency must either be a 3 letter iso code " +
                "or a 20 byte hash encoded in hexadecimal"
            );
        }

        /// <summary>
        /// Create a Currency for Oracle PriceData fields (XLS-47 format).
        /// Uses left-aligned ASCII encoding (bytes 0-2) for 3-letter codes,
        /// or direct hex for 40-character non-standard currencies.
        /// This differs from standard IOU currencies which use bytes 12-14.
        /// </summary>
        /// <param name="str">Currency code (XRP, 3-letter code, or 40-hex).</param>
        /// <returns>Currency with XLS-47 Oracle encoding.</returns>
        public static Currency FromOracleString(string str)
        {
            if (str == "XRP")
            {
                return Xrp;
            }
            switch (str.Length)
            {
                case 40:
                    // Non-standard currency: use hex bytes directly
                    return new Currency(B16.Decode(str));
                case 3:
                    // XLS-47 Oracle format: left-aligned at bytes 0-2
                    return new Currency(EncodeOracleCurrency(str));
            }
            throw new InvalidOperationException(
                "Currency must either be a 3 letter iso code " +
                "or a 20 byte hash encoded in hexadecimal"
            );
        }

        /// <summary>
        /// Encode currency for Oracle XLS-47 format (left-aligned at bytes 0-2).
        /// This format is used for BaseAsset and QuoteAsset in PriceData objects.
        /// </summary>
        /// <param name="currencyCode">3-letter currency code.</param>
        /// <returns>20-byte array with currency at bytes 0-2.</returns>
        public static byte[] EncodeOracleCurrency(string currencyCode)
        {
            byte[] currencyBytes = new byte[20];
            currencyBytes[0] = (byte)char.ConvertToUtf32(currencyCode, 0);
            currencyBytes[1] = (byte)char.ConvertToUtf32(currencyCode, 1);
            currencyBytes[2] = (byte)char.ConvertToUtf32(currencyCode, 2);
            return currencyBytes;
        }

        /// <summary>
        /// Legacy method for standard XRPL currency encoding (bytes 12-14).
        /// Used for issued currency tokens, not Oracle PriceData.
        /// </summary>
        /// <param name="currencyCode">currency code</param>
        /// <returns></returns>
        public static byte[] EncodeCurrency(string currencyCode)
        {
            byte[] currencyBytes = new byte[20];
            currencyBytes[12] = (byte)char.ConvertToUtf32(currencyCode, 0);
            currencyBytes[13] = (byte)char.ConvertToUtf32(currencyCode, 1);
            currencyBytes[14] = (byte)char.ConvertToUtf32(currencyCode, 2);
            return currencyBytes;
        }

        public static implicit operator Currency(string v)
        {
            return FromString(v);
        }
        public static implicit operator Currency(JsonNode v)
        {
            return FromJson(v);
        }
        public static implicit operator JsonNode(Currency v)
        {
            return JsonValue.Create(v.ToString());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (IsoCode != null)
            {
                return IsoCode;
            }
            return base.ToString();
        }
        /// <summary>
        /// Defines how to read a Currency from a BinaryParser
        /// </summary>
        /// <param name="parser">The binary parser to read Currency</param>
        /// <returns>A Blob object</returns>
        public new static Currency FromParser(BinaryParser parser, int? hint = null)
        {
            return new Currency(parser.Read(20));
        }
    }
}