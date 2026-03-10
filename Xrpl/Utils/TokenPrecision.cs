using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using Xrpl.Models.Common;

[assembly: InternalsVisibleTo("Xrpl.Tests")]

namespace Xrpl.Utils
{
    /// <summary>
    /// Enum specifying the rounding mode for token amount precision.
    /// </summary>
    internal enum PrecisionRoundingMode
    {
        /// <summary>
        /// Truncate excess decimal places without rounding.
        /// </summary>
        Truncate,

        /// <summary>
        /// Round to the nearest value using "away from zero" rounding.
        /// </summary>
        Round,
    }

    /// <summary>
    /// Provides token amount rounding following XRPL precision rules.
    /// XRP has 6 decimal places, non-XRP tokens have up to 15 significant digits.
    /// </summary>
    internal static class TokenPrecision
    {
        private const int XrpDecimals = 6;
        private const int NonXrpMaxDecimals = 15;

        /// <summary>
        /// Rounds a nullable token amount according to XRPL precision rules.
        /// </summary>
        /// <param name="value">The token amount to round, or null.</param>
        /// <param name="isXrp">True if the token is XRP; false for other tokens.</param>
        /// <param name="mode">The rounding mode to apply. Defaults to Truncate.</param>
        /// <returns>The rounded token amount, or null if the input is null.</returns>
        public static decimal? RoundTokenAmount(
            this decimal? value,
            bool isXrp,
            PrecisionRoundingMode mode = PrecisionRoundingMode.Truncate)
        {
            if (value is null)
                return null;
            return RoundTokenAmount(value.Value, isXrp, mode);
        }

        /// <summary>
        /// Rounds a token amount according to XRPL precision rules.
        /// </summary>
        /// <param name="value">The token amount to round.</param>
        /// <param name="isXrp">True if the token is XRP; false for other tokens.</param>
        /// <param name="mode">The rounding mode to apply. Defaults to Truncate.</param>
        /// <returns>The rounded token amount.</returns>
        public static decimal RoundTokenAmount(
            this decimal value,
            bool isXrp,
            PrecisionRoundingMode mode = PrecisionRoundingMode.Truncate)
        {
            var decimals = isXrp
                ? XrpDecimals
                : GetNonXrpDecimals(value);
            return mode switch
            {
                PrecisionRoundingMode.Truncate => TruncateDecimals(value, decimals),
                PrecisionRoundingMode.Round => Math.Round(value, decimals, MidpointRounding.AwayFromZero),
                _ => value,
            };
        }

        /// <summary>
        /// Formats a token amount as a string following XRPL precision rules.
        /// </summary>
        /// <param name="value">The token amount to format.</param>
        /// <param name="isXrp">True if the token is XRP; false for other tokens.</param>
        /// <param name="mode">The rounding mode to apply. Defaults to Truncate.</param>
        /// <returns>The formatted token amount as a string using invariant culture.</returns>
        public static string FormatTokenAmount(
            this decimal value,
            bool isXrp,
            PrecisionRoundingMode mode = PrecisionRoundingMode.Truncate)
        {
            var rounded = RoundTokenAmount(value, isXrp, mode);
            return rounded.ToString(format: "0.#############################", CultureInfo.InvariantCulture);
        }

        private static int GetNonXrpDecimals(decimal value)
        {
            var abs = Math.Abs(value);
            if (abs >= 1m)
            {
                var intDigits = decimal.Truncate(abs)
                    .ToString(format: "0", CultureInfo.InvariantCulture).Length;
                var allowed = NonXrpMaxDecimals - intDigits;
                return allowed < 0 ? 0 : allowed;
            }
            return NonXrpMaxDecimals;
        }

        private static decimal TruncateDecimals(decimal value, int decimals)
        {
            if (decimals < 0)
                decimals = 0;
            var factor = Pow10(decimals);
            return Math.Truncate(value * factor) / factor;
        }

        private static decimal Pow10(int exponent)
        {
            var result = 1m;
            for (var i = 0; i < exponent; i++)
                result *= 10m;
            return result;
        }

        /// <summary>
        /// Truncates the currency value according to XRPL precision rules.
        /// </summary>
        /// <param name="currency">The currency object to truncate. Returns null if input is null.</param>
        /// <returns>The currency object with truncated value, or null if the input is null.</returns>
        public static Currency TruncateValue(this Currency currency)
        {
            if (currency is null)
                return null;
            bool isXrp = currency.CurrencyCode?.ToUpper() == "XRP" && currency.Issuer == null;
            if (isXrp)
                currency.ValueAsXrp = currency.ValueAsXrp.RoundTokenAmount(true, PrecisionRoundingMode.Truncate);
            else
                currency.ValueAsNumber = currency.ValueAsNumber.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
            return currency;
        }
    }
}
