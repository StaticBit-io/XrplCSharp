using System;
using System.Globalization;
using System.Text.Json.Serialization;
using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/bookOffers.ts
//https://github.com/XRPLF/rippled/blob/develop/src/xrpld/rpc/handlers/orderbook/BookOffers.cpp

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// One side of an order book: XRP, an issued currency, or an MPT (XLS-33).<br/>
    /// An issued currency names <see cref="Currency"/> and <see cref="Issuer"/>, XRP names only
    /// <see cref="Currency"/>, and an MPT names only <see cref="MptIssuanceId"/>.
    /// </summary>
    public class TakerAmount
    {
        /// <summary>
        /// The standard format for currency codes is a three-character string such as USD.<br/>
        /// This is intended for use with ISO 4217 Currency Codes <br/>
        /// As a 160-bit hexadecimal string, such as "0158415500000000C1F76FF6ECB0BAC600000000".<br/>
        /// The following characters are permitted:<br/>
        /// all uppercase and lowercase letters, digits, as well as the symbols ? ! @ # $ % ^ * ( ) { } [ ] | and symbols ampersand, less, greater<br/>
        /// Currency codes are case-sensitive.<br/>
        /// MUST NOT be combined with <see cref="MptIssuanceId"/>.
        /// </summary>
        [JsonPropertyName("currency")]
        public string Currency { get; set; }
        /// <summary>
        /// Generally, the account that issues this token.<br/>
        /// In special cases, this can refer to the account that holds the token instead.<br/>
        /// MUST NOT be combined with <see cref="MptIssuanceId"/>.
        /// </summary>
        [JsonPropertyName("issuer")]
        public string Issuer { get; set; }
        /// <summary>
        /// The MPT issuance whose book to read, as its 48-character hex ID.<br/>
        /// MPT order books exist only under the MPTokensV2 amendment (XLS-82).<br/>
        /// MUST NOT be combined with <see cref="Currency"/> or <see cref="Issuer"/>.
        /// </summary>
        [JsonPropertyName("mpt_issuance_id")]
        public string MptIssuanceId { get; set; }

        /// <summary>The side of a book that trades <paramref name="asset"/>: XRP, an issued currency or an MPT.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
        public static TakerAmount FromAsset(IssuedCurrency asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            if (asset.IsMpt())
                return new TakerAmount { MptIssuanceId = asset.MptIssuanceId };

            return asset.IsXrp()
                ? new TakerAmount { Currency = "XRP" }
                : new TakerAmount { Currency = asset.Currency, Issuer = asset.Issuer };
        }
    }
    /// <summary>
    /// The book_offers method retrieves a list of offers, also known as the order.<br/>
    /// Book, between two currencies.<br/>
    /// Returns an  <see cref="BookOffers"/>.
    /// </summary>
    /// <code>
    /// {
    /// 	"id": 4,
    /// 	"command": "book_offers",
    /// 	"taker": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
    /// 	"taker_gets":{
    /// 	"currency": "XRP"},
    /// 	"taker_pays":{
    /// 	"currency": "USD",
    /// 	"issuer": "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"},
    /// 	"limit": 10}
    /// </code>
    public class BookOffersRequest : BaseLedgerRequest
    {
        public BookOffersRequest()
        {
            Command = "book_offers";
        }
        /// <summary>
        /// If provided, the server does not provide more than this many offers in the results.<br/>
        /// The total number of results returned may be fewer than the limit, because the server omits unfunded offers.
        /// </summary>
        [JsonPropertyName("limit")]
        public uint? Limit { get; set; }
        /// <summary>
        /// The Address of an account to use as a perspective.<br/>
        /// Unfunded offers placed by this account are always included in the response.
        /// </summary>
        [JsonPropertyName("taker")]
        public string Taker { get; set; }
        /// <summary>
        /// Specification of which currency the account taking the offer would receive,
        /// as an object with currency and issuer fields (omit issuer for XRP), like currency amounts,
        /// or with mpt_issuance_id alone for an MPT.
        /// </summary>
        [JsonPropertyName("taker_gets")]
        public TakerAmount TakerGets { get; set; }
        /// <summary>
        /// Specification of which currency the account taking the offer would pay,
        /// as an object with currency and issuer fields (omit issuer for XRP), like currency amounts,
        /// or with mpt_issuance_id alone for an MPT.
        /// </summary>
        [JsonPropertyName("taker_pays")]
        public TakerAmount TakerPays { get; set; }

        /// <summary>
        /// The permissioned domain whose book to read, as its 64-character ledger index;
        /// omitted for the open book.
        /// </summary>
        [JsonPropertyName("domain")]
        public string Domain { get; set; }

        private const string TakerPaysName = "taker_pays";
        private const string TakerGetsName = "taker_gets";
        private const string IsoCharSet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<>(){}[]|?!@#$%^&*";
        private const int CurrencyHexLength = 40;
        private const int AccountHexLength = 40;
        private const int MptIssuanceIdHexLength = 48;
        private const int DomainHexLength = 64;

        /// <summary>
        /// Checks the request as rippled's <c>book_offers</c> handler does, in the same order, so a
        /// request the node would refuse is refused before it is sent. <see cref="Xrpl.Client.IXrplClient.BookOffers"/>
        /// calls it.
        /// </summary>
        /// <exception cref="ValidationException">
        /// A side is missing, names an MPT together with a currency or an issuer, carries a currency,
        /// issuer or MPT issuance ID the node cannot parse, gives XRP an issuer or an issued currency
        /// none; the taker is not an account or the domain not a 64-character hex ID; or both sides
        /// name the same asset. The message names the field and the rippled error code.
        /// </exception>
        public void Validate()
        {
            if (TakerPays == null)
                throw new ValidationException($"Missing field '{TakerPaysName}'.");
            if (TakerGets == null)
                throw new ValidationException($"Missing field '{TakerGetsName}'.");

            ValidateShape(TakerPays, TakerPaysName);
            ValidateShape(TakerGets, TakerGetsName);

            byte[] payCurrency = ParseAsset(TakerPays, TakerPaysName, "srcCurMalformed");
            byte[] getCurrency = ParseAsset(TakerGets, TakerGetsName, "dstAmtMalformed");

            byte[] payIssuer = ParseIssuer(TakerPays, payCurrency, TakerPaysName, "srcIsrMalformed");
            byte[] getIssuer = ParseIssuer(TakerGets, getCurrency, TakerGetsName, "dstIsrMalformed");

            if (Taker != null && !XrplCodec.IsValidClassicAddress(Taker))
                throw new ValidationException("Invalid field 'taker'. (invalidParams)");

            if (Domain != null && !IsHex(Domain, DomainHexLength))
                throw new ValidationException("Unable to parse domain. (domainMalformed)");

            if (SameAsset(TakerPays, payCurrency, payIssuer, TakerGets, getCurrency, getIssuer))
                throw new ValidationException("taker_gets same as taker_pays. (badMarket)");
        }

        /// <summary><c>validateTakerJSON</c>: a currency or an MPT issuance ID, never both.</summary>
        private static void ValidateShape(TakerAmount side, string name)
        {
            if (side.Currency == null && side.MptIssuanceId == null)
                throw new ValidationException($"Missing field '{name}.currency'.");

            if (side.MptIssuanceId != null && (side.Currency != null || side.Issuer != null))
                throw new ValidationException($"Invalid field '{name}': mpt_issuance_id cannot be combined with currency or issuer.");
        }

        /// <summary>
        /// <c>parseTakerAssetJSON</c>: the 160-bit currency of an issued currency or XRP, or null for
        /// an MPT whose issuance ID parses.
        /// </summary>
        private static byte[] ParseAsset(TakerAmount side, string name, string code)
        {
            if (side.Currency != null)
            {
                byte[] currency = ParseCurrency(side.Currency);
                if (currency == null)
                    throw new ValidationException($"Invalid field '{name}.currency', bad currency. ({code})");
                return currency;
            }

            if (!IsHex(side.MptIssuanceId, MptIssuanceIdHexLength))
                throw new ValidationException($"Invalid field '{name}.mpt_issuance_id'. ({code})");
            return null;
        }

        /// <summary>
        /// <c>parseTakerIssuerJSON</c>: the issuer of an issued currency, all zeros for XRP; nothing
        /// for an MPT.
        /// </summary>
        private static byte[] ParseIssuer(TakerAmount side, byte[] currency, string name, string code)
        {
            if (currency == null)
                return null;

            byte[] issuer = new byte[20];
            if (side.Issuer != null)
            {
                issuer = ParseAccount(side.Issuer);
                if (issuer == null)
                    throw new ValidationException($"Invalid field '{name}.issuer', bad issuer. ({code})");
                if (IsAccountOne(issuer))
                    throw new ValidationException($"Invalid field '{name}.issuer', bad issuer account one. ({code})");
            }

            bool xrpCurrency = IsZero(currency);
            bool xrpIssuer = IsZero(issuer);
            if (xrpCurrency && !xrpIssuer)
                throw new ValidationException($"Unneeded field '{name}.issuer' for XRP currency specification. ({code})");
            if (!xrpCurrency && xrpIssuer)
                throw new ValidationException($"Invalid field '{name}.issuer', expected non-XRP issuer. ({code})");

            return issuer;
        }

        private static bool SameAsset(TakerAmount pays, byte[] payCurrency, byte[] payIssuer, TakerAmount gets, byte[] getCurrency, byte[] getIssuer)
        {
            if (payCurrency == null || getCurrency == null)
            {
                return payCurrency == null && getCurrency == null
                    && string.Equals(pays.MptIssuanceId, gets.MptIssuanceId, StringComparison.OrdinalIgnoreCase);
            }

            return payCurrency.AsSpan().SequenceEqual(getCurrency) && payIssuer.AsSpan().SequenceEqual(getIssuer);
        }

        /// <summary>
        /// <c>toCurrency</c>: empty or <c>XRP</c> is XRP, three characters of the ISO set sit at
        /// bytes 12-14, anything else must be 40 hex digits.
        /// </summary>
        private static byte[] ParseCurrency(string code)
        {
            byte[] currency = new byte[20];
            if (code.Length == 0 || code == "XRP")
                return currency;

            if (code.Length == 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (IsoCharSet.IndexOf(code[i]) < 0)
                        return null;
                    currency[12 + i] = (byte)code[i];
                }

                return currency;
            }

            return TryParseHex(code, CurrencyHexLength);
        }

        /// <summary><c>toIssuer</c>: 40 hex digits, or a classic address.</summary>
        private static byte[] ParseAccount(string issuer)
        {
            byte[] hex = TryParseHex(issuer, AccountHexLength);
            if (hex != null)
                return hex;

            return XrplCodec.IsValidClassicAddress(issuer) ? XrplCodec.DecodeAccountID(issuer) : null;
        }

        private static bool IsHex(string value, int length) => TryParseHex(value, length) != null;

        private static byte[] TryParseHex(string value, int length)
        {
            if (value == null || value.Length != length)
                return null;

            byte[] bytes = new byte[length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(value.AsSpan(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
                    return null;
            }

            return bytes;
        }

        private static bool IsZero(byte[] bytes) => Array.TrueForAll(bytes, b => b == 0);

        private static bool IsAccountOne(byte[] account)
        {
            for (int i = 0; i < account.Length - 1; i++)
            {
                if (account[i] != 0)
                    return false;
            }

            return account[account.Length - 1] == 1;
        }
    }
}
