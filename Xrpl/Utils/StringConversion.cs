using System;
using System.Text;

using Xrpl.Models.Utils;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/stringConversion.ts

namespace Xrpl.Utils
{
    /// <summary>
    /// Canonical text ↔ hex conversion extensions. Hex output is UPPERCASE —
    /// the convention rippled uses in every JSON response — so SDK-generated
    /// hex compares Ordinal-equal against node output.
    /// </summary>
    public static class StringConversion
    {
        /// <summary>
        /// Encodes a UTF-8 string as an UPPERCASE hex string.
        /// </summary>
        /// <param name="input">string</param>
        /// <returns></returns>
        public static string ConvertStringToHex(this string input)
        {
            return Convert.ToHexString(Encoding.UTF8.GetBytes(input));
        }

        /// <summary>
        /// Decodes a UTF-8 hex string back to readable text. Bytes are decoded
        /// as-is (no trailing-null trimming) — pass the string through
        /// <see cref="HexStringHelper.FromHex(string, bool)"/> with
        /// <c>trimTrailingNulls: true</c> for zero-padded fixed-size fields.
        /// </summary>
        /// <param name="input">UTF8 HEX string</param>
        /// <returns></returns>
        public static string FromHexString(this string input)
        {
            return HexStringHelper.FromHex(input, trimTrailingNulls: false);
        }
    }
}
