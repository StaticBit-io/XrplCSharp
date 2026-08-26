using System.Text.Json.Serialization;

using System;
using System.Globalization;
using Xrpl.Client.Exceptions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Xrpl.Client.Extensions;
using Xrpl.Utils;
using Xrpl.Models.Methods;
using Xrpl.Models.Utils;
using Xrpl.Utils.Hashes;

//https://xrpl.org/currency-formats.html#currency-formats
//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/common/index.ts

namespace Xrpl.Models.Common;

/// <summary>
/// The XRP Ledger has two kinds of digital asset: XRP and tokens.<br/>
/// Both types have high precision, although their formats are different
/// </summary>
public class Currency
{
    private string _mpTokenIssuanceId;

    /// <summary>
    /// base constructor.
    /// </summary>
    public Currency() { CurrencyCode = "XRP"; }

    /// <summary>
    /// The ID of the MPT to authorize.
    /// </summary>
    [JsonPropertyName("mpt_issuance_id")]
    public string MPTokenIssuanceID
    {
        get => _mpTokenIssuanceId;

        set
        {
            _mpTokenIssuanceId = value;
            CurrencyCode = null;
        }
    }

    /// <summary>
    /// The standard format for currency codes is a three-character string such as USD.<br/>
    /// This is intended for use with ISO 4217 Currency Codes <br/>
    /// As a 160-bit hexadecimal string, such as "0158415500000000C1F76FF6ECB0BAC600000000".<br/>
    /// The following characters are permitted:<br/>
    /// all uppercase and lowercase letters, digits, as well as the symbols ? ! @ # $ % ^ * ( ) { } [ ] | and symbols ampersand, less, greater<br/>
    /// Currency codes are case-sensitive.
    /// </summary>
    [JsonPropertyName("currency")]
    public string CurrencyCode { get; set; }

    /// <summary>
    /// Quoted decimal representation of the amount of the token.<br/>
    /// This can include scientific notation, such as 1.23e11 meaning 123,000,000,000.<br/>
    /// Both e and E may be used.<br/>
    /// This can be negative when displaying balances, but negative values are disallowed in other contexts such as specifying how much to send.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; }

    /// <summary>
    /// Generally, the account that issues this token.<br/>
    /// In special cases, this can refer to the account that holds the token instead.
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    /// <summary>
    /// Readable currency name 
    /// </summary>
    [JsonIgnore]
    public string CurrencyValidName => CurrencyCode.CurrencyReadableName();

    /// <summary>
    /// What the ledger's amount string allows: a sign, a decimal point and an exponent, and
    /// nothing else. Surrounding whitespace is not accepted, because the node never sends it.
    /// </summary>
    private const NumberStyles AmountStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>
    /// decimal currency amount (drops for XRP)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="decimal"/> does not cover the range XRPL allows an issued currency: the ledger
    /// reaches roughly <c>1e96</c> and down to <c>1e-81</c>, this type stops at about
    /// <c>7.9e28</c>. Amounts above that throw <see cref="AmountOutOfRangeException"/> rather than
    /// being answered with a number that is not the one the node sent.
    /// </para>
    /// <para>
    /// Amounts below <c>1e-28</c> return zero instead of throwing, and the asymmetry is deliberate.
    /// A balance of <c>1e-81</c> rounded to zero is zero at any scale a caller can act on, so
    /// failing over it would cost more than it protects; an amount of <c>1e96</c> reported as
    /// <c>7.9e28</c> is wrong by 67 orders of magnitude and is worth stopping for.
    /// </para>
    /// </remarks>
    /// <exception cref="AmountOutOfRangeException">The amount exceeds what <see cref="decimal"/> can hold.</exception>
    /// <exception cref="FormatException">The amount is not a number at all.</exception>
    [JsonIgnore]
    public decimal ValueAsNumber
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                return 0;
            }

            if (decimal.TryParse(Value, AmountStyles, CultureInfo.InvariantCulture, out decimal amount))
            {
                return amount;
            }

            // Tell the two failures apart rather than reporting both as a bad format. double
            // spans the whole ledger range, so parsing there succeeds exactly when the string is
            // a real number that decimal simply cannot hold.
            //
            // IsFinite matters: double.TryParse accepts "NaN", "Infinity" and "-Infinity" whatever
            // NumberStyles it is given, because those symbols are matched separately from the
            // numeric ones. Without the check, a string that is not a quantity at all would be
            // reported as a quantity too large - which is the sort of confident wrong answer this
            // property is being changed to stop giving.
            if (double.TryParse(Value, AmountStyles, CultureInfo.InvariantCulture, out double asDouble)
                && double.IsFinite(asDouble))
            {
                throw new AmountOutOfRangeException(Value);
            }

            throw new FormatException(
                $"The amount '{Value}' is not a number in the form the XRP Ledger uses.");
        }

        set => Value = value.ToString(
            CurrencyCode == "XRP"
                ? "G0"
                : "G16",
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// XRP token amount (non drops value)
    /// </summary>
    [JsonIgnore]
    public decimal? ValueAsXrp
    {
        get
        {
            if (CurrencyCode != "XRP" || string.IsNullOrWhiteSpace(Value))
            {
                return null;
            }

            return ValueAsNumber / 1000000;
        }
        set
        {
            if (value.HasValue)
            {
                CurrencyCode = "XRP";
                var val = value.Value * 1000000;
                Value = val.ToString(format: "G0", CultureInfo.InvariantCulture);
            }
            else
            {
                Value = "0";
            }
        }
    }

    #region Overrides of Object

    /// <summary>
    /// A readable form of the amount.
    /// </summary>
    /// <remarks>
    /// Falls back to the raw <see cref="Value"/> for an amount outside what <see cref="decimal"/>
    /// can hold, rather than letting <see cref="ValueAsNumber"/> throw through it. By convention
    /// <see cref="object.ToString"/> does not throw, and the places it is called from - logging,
    /// string interpolation, a debugger's watch window - are exactly where someone would be while
    /// working out why an amount is unusual. Failing there hides the value instead of showing it.
    /// </remarks>
    public override string ToString()
    {
        try
        {
            return CurrencyValidName == "XRP"
                ? $"XRP: {ValueAsXrp:0.######}"
                : $"{CurrencyValidName}: {ValueAsNumber:0.###############}";
        }
        catch (Exception exception) when (exception is AmountOutOfRangeException or FormatException)
        {
            return $"{CurrencyValidName}: {Value}";
        }
    }

    public override bool Equals(object o) { return o is Currency model && model.Issuer == Issuer && model.CurrencyCode == CurrencyCode; }

    public static bool operator ==(Currency? left, Currency? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }


    public static bool operator !=(Currency? left, Currency? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Implicit conversion from string → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(string value)
        => new Currency { Value = value };
    /// <summary>
    /// Implicit conversion from decimal → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(decimal value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    /// <summary>
    /// Implicit conversion from double → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(double value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    /// <summary>
    /// Implicit conversion from int → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(int value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    /// <summary>
    /// Implicit conversion from uint → Currency
    /// </summary>
    /// <param name="value">value</param>
    /// <returns>currency</returns>
    public static implicit operator Currency(uint value)
        => new Currency { Value = value.ToString(CultureInfo.InvariantCulture) };
    #endregion
}

public static class CurrencyExtensions
{
    /// <summary>
    /// Returns the human-readable value of this currency amount.
    /// For XRP, returns the value in XRP (drops / 1,000,000).
    /// For tokens, returns the raw numeric value.
    /// </summary>
    /// <param name="currency">The currency to get the value from.</param>
    /// <param name="round">Optional number of decimal places to round to.</param>
    /// <returns>The decimal value, or null if the currency is null.</returns>
    public static decimal? GetValue(this Currency currency, int? round = null)
        => currency is null
            ? null
            : currency.CurrencyCode is "XRP"
                ? round is { } r1 && currency.ValueAsXrp is { } v1 ? Math.Round(v1, r1) : currency.ValueAsXrp
                : round is { } r2 ? Math.Round(currency.ValueAsNumber, r2) : currency.ValueAsNumber;

    /// <summary>
    /// Determines whether this currency represents XRP.
    /// A currency is XRP if its code (case-insensitive) is "XRP" and it has no issuer.
    /// </summary>
    /// <param name="currency">The currency to check.</param>
    /// <returns><c>true</c> if this currency is XRP; otherwise, <c>false</c>.</returns>
    public static bool IsXrp(this Currency currency)
        => currency is not null && currency.CurrencyCode?.ToUpper() is "XRP" && currency.Issuer == null;

    public static Common.IssuedCurrency ToIssued(this Currency currency) =>
        new Common.IssuedCurrency()
        {
            Currency = currency.CurrencyCode,
            Issuer = currency.Issuer,
        };
    /// <summary>
    /// check that currency is NFT XLS14D
    /// </summary>
    /// <param name="cur"></param>
    /// <returns></returns>
    public static bool IsNFT14D(this Currency cur) => cur is { } c && c.CurrencyCode.StartsWith("02");
    //cur is { ValueAsNumber: 0.000000000000000000000000000000000000000000000000000000000000000000000000000000001m };

    /// <summary> get readable token code </summary>
    /// <param name="currencyCode">token code</param>
    /// <returns>readable token code</returns>
    public static string CurrencyReadableName(this string currencyCode)
    {
        if (!IsValidCurrencyCode(currencyCode))
        {
            return string.Empty;
        }

        return NormalizeCurrencyCode(currencyCode);
    }

    private static bool IsValidCurrencyCode(string currencyCode) =>
        !string.IsNullOrEmpty(currencyCode) && currencyCode.Length > 0;

    public static bool IsLpToken(this Currency currency)
    {
        return currency.CurrencyCode.IsLpToken();
    }

    /// <summary>
    /// Whether this amount is a multi-purpose token - that is, whether it carries an issuance id.
    /// </summary>
    /// <remarks>
    /// An issuance id is what distinguishes a multi-purpose token from every other kind of amount,
    /// so this is exclusive with <see cref="IsXrp"/> and with an issued currency: an amount is at
    /// most one of the three.
    /// </remarks>
    public static bool IsMPTToken(this Currency currency)
    {
        return currency is not null && !string.IsNullOrWhiteSpace(currency.MPTokenIssuanceID);
    }
    public static bool IsLpToken(this TrustLine currency)
    {
        return currency.Currency.IsLpToken();
    }
    public static bool IsLpToken(this string currencyCode)
    {
        return !string.IsNullOrWhiteSpace(currencyCode) && currencyCode.IsHexCurrencyCode() && currencyCode.StartsWith("03");
    }

    public static string NormalizeCurrencyCode(this string currencyCode, int maxLength = 20)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return currencyCode;

        // Стандартный 3-символьный код
        if (currencyCode.Length == 3)
        {
            return currencyCode.Trim();
        }

        // Проверка на 40-символьный шестнадцатеричный код
        if (currencyCode.IsHexCurrencyCode())
        {
            string hex = currencyCode;

            // Устаревший код с демереджем (начинается с 01)
            if (hex.StartsWith("01"))
            {
                return ConvertDemurrageToUTF8(currencyCode);
            }

            // XLS-16d NFT Metadata (начинается с 02)
            if (hex.StartsWith("02"))
            {
                string xlf15d = Encoding.UTF8.GetString(HexToBytes(hex)).Substring(8, Math.Min(maxLength, hex.Length / 2 - 8)).Trim();
                if (Regex.IsMatch(xlf15d, "[a-zA-Z0-9]{3,}") && xlf15d.ToLower() != "xrp")
                {
                    return xlf15d;
                }
            }

            if (hex.StartsWith("03"))
            {
                return $"LP {currencyCode[2..6]}..";
            }

            // Обычный шестнадцатеричный код
            var decodedHex = hex.FromHexString().Replace("\0", null).Trim('\0');
            if (string.IsNullOrWhiteSpace(decodedHex))
            {
                return currencyCode;
            }
            return decodedHex;
        }

        return currencyCode;
    }

    public static bool NormalCurrency(this string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return false;

        // Стандартный 3-символьный код
        if (currencyCode.Length == 3 && currencyCode.Trim().ToLower() != "xrp")
        {
            return true;
        }

        // Проверка на 40-символьный шестнадцатеричный код
        if (currencyCode.IsHexCurrencyCode())
        {
            string hex = currencyCode;

            // Устаревший код с демереджем (начинается с 01)
            if (hex.StartsWith("01"))
            {
                return false;
            }

            // XLS-16d NFT Metadata (начинается с 02)
            if (hex.StartsWith("02"))
            {
                return false;
            }

            if (hex.StartsWith("03"))
            {
                return false;
            }

            // Обычный шестнадцатеричный код
            return true;
        }

        return false;
    }
    static string ConvertDemurrageToUTF8(string demurrageCode)
    {
        byte[] bytes = HexToBytes(demurrageCode);
        string code = $"{(char)bytes[1]}{(char)bytes[2]}{(char)bytes[3]}";

        // Вычисление процентной ставки
        int interestStart = (bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7];
        double interestPeriod = BitConverter.ToDouble(bytes.Skip(8).Take(8).Reverse().ToArray(), 0);
        const int yearSeconds = 31536000; // Фиксированное количество секунд в году
        double interestAfterYear = Math.Pow(Math.E, (interestStart + yearSeconds - interestStart) / interestPeriod);
        double interest = (interestAfterYear * 100) - 100;

        return $"{code} ({interest:F1}% pa)";
    }
    static byte[] HexToBytes(string hex)
    {
        return Enumerable.Range(0, hex.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
            .ToArray();
    }

}