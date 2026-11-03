using System;

namespace Xrpl.Models.Common
{
    /// <summary>
    /// Case-correct identity comparisons for currency codes and issuer addresses, for code that
    /// prices, routes or matches assets (quotes, books, pools, trust lines).
    /// </summary>
    /// <remarks>
    /// Ledger rules this type encodes:
    /// <list type="bullet">
    /// <item>
    /// A 3-character (or otherwise non-hex) currency code is case-sensitive: "XPM", "xpm" and
    /// "Xpm" are three tokens, possibly with the same issuer. The ledger refuses an IOU with the
    /// exact code "XRP" (<c>temBAD_CURRENCY</c>), so an ordinal match on "XRP" identifies native
    /// XRP, and a lowercase "xrp" IOU is a different, legal asset.
    /// </item>
    /// <item>
    /// A 40-character hex code (a non-standard currency name, or an LP token starting with "03")
    /// is the hex rendering of 20 bytes. "58504D..." and "58504d..." are the same token; sources
    /// differ in hex casing, so two hex codes compare case-insensitively.
    /// </item>
    /// <item>An issuer is a base58 address and compares ordinally.</item>
    /// </list>
    /// User-facing search and filter inputs (a token picker's name filter) should stay
    /// case-insensitive; this type is for identity, not search.
    /// </remarks>
    public static class AssetIdentity
    {
        private const string NativeXrpCode = "XRP";

        private const int HexCurrencyCodeLength = 40;

        /// <summary>
        /// Whether <paramref name="code"/> is exactly 40 hex digits in either case: a non-standard
        /// currency code or an LP token.
        /// </summary>
        /// <remarks>
        /// Accepts <c>null</c> (a code read from a DTO may be absent) and scans the string instead
        /// of using a regular expression, because asset comparison runs per candidate while routes
        /// are enumerated.
        /// </remarks>
        public static bool IsHexCurrencyCode(string code)
        {
            if (code is null || code.Length != HexCurrencyCodeLength)
            {
                return false;
            }

            foreach (char c in code)
            {
                bool isHexDigit = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');

                if (!isHexDigit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether two currency codes are the same currency: two 40-digit hex codes compare
        /// case-insensitively, anything else ordinally (a hex code never equals a non-hex code).
        /// </summary>
        public static bool CurrencyEquals(string a, string b)
        {
            if (IsHexCurrencyCode(a) && IsHexCurrencyCode(b))
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(a, b, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether two issuer addresses are the same account. Ordinal: a classic address has no
        /// case-insensitive form.
        /// </summary>
        public static bool IssuerEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether two assets are the same: <see cref="CurrencyEquals"/> on the codes and
        /// <see cref="IssuerEquals"/> on the issuers. A null, empty or whitespace issuer is the
        /// empty issuer, so two native-XRP assets are equal however they spell "no issuer".
        /// </summary>
        public static bool AssetEquals(string codeA, string issuerA, string codeB, string issuerB)
        {
            if (!CurrencyEquals(codeA, codeB))
            {
                return false;
            }

            string normalizedIssuerA = string.IsNullOrWhiteSpace(issuerA) ? string.Empty : issuerA;
            string normalizedIssuerB = string.IsNullOrWhiteSpace(issuerB) ? string.Empty : issuerB;

            return IssuerEquals(normalizedIssuerA, normalizedIssuerB);
        }

        /// <summary>
        /// Whether <paramref name="code"/> is exactly "XRP", compared ordinally. The ledger refuses
        /// an IOU with that exact code, so the code alone identifies native XRP; "xrp" and "Xrp"
        /// are legal IOU codes and do not match.
        /// </summary>
        public static bool IsXrp(string code)
        {
            return string.Equals(code, NativeXrpCode, StringComparison.Ordinal);
        }

        /// <summary>
        /// <see cref="IsXrp(string)"/> that also requires an empty issuer. Native XRP has none, so
        /// "XRP" next to an issuer is malformed input and not an asset.
        /// </summary>
        public static bool IsXrp(string code, string issuer)
        {
            return IsXrp(code) && string.IsNullOrWhiteSpace(issuer);
        }
    }
}
