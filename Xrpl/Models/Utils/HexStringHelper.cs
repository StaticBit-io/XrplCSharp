using System;
using System.Text;

namespace Xrpl.Models.Utils
{
    /// <summary>
    /// Provides utility methods for normalizing and converting variable-length
    /// hex-encoded string fields used throughout the XRP Ledger protocol.
    /// Fields such as CredentialType, URI, and DIDDocument are stored on-chain
    /// as hex-encoded binary blobs. This helper converts between human-readable
    /// text and the canonical hex representation.
    /// </summary>
    public static class HexStringHelper
    {
        /// <summary>
        /// Normalizes a value for a hex-encoded VL field. If the value is already
        /// valid hex, it is uppercased and returned as-is. Otherwise, the value is
        /// treated as UTF-8 text and encoded to hex.
        /// </summary>
        /// <param name="value">Raw value (hex string or plain text).</param>
        /// <param name="maxBytes">Maximum allowed byte length (e.g. 64 for CredentialType).</param>
        /// <param name="fieldName">Field name for error messages.</param>
        /// <param name="padToBytes">If greater than zero, the result is right-padded with
        /// zero bytes to exactly this size. Use for fixed-length fields such as
        /// UInt256 / Hash256 (32 bytes). Defaults to 0 (no padding).</param>
        /// <returns>Uppercased hex string, or null if input is null/empty.</returns>
        public static string NormalizeToHex(string value, int maxBytes = 0, string fieldName = null, int padToBytes = 0)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            if (IsValidHex(value))
            {
                var upper = value.ToUpperInvariant();
                if (maxBytes > 0 && upper.Length > maxBytes * 2)
                    throw new ArgumentException(
                        $"{fieldName ?? "Field"} cannot exceed {maxBytes} bytes ({maxBytes * 2} hex characters)");
                if (padToBytes > 0 && upper.Length > padToBytes * 2)
                    throw new ArgumentException(
                        $"{fieldName ?? "Field"} cannot exceed {padToBytes} bytes ({padToBytes * 2} hex characters)");
                if (padToBytes > 0)
                    return upper.PadRight(padToBytes * 2, '0');
                return upper;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            if (maxBytes > 0 && bytes.Length > maxBytes)
                throw new ArgumentException(
                    $"{fieldName ?? "Field"} text is too long (max {maxBytes} bytes UTF-8)");
            if (padToBytes > 0 && bytes.Length > padToBytes)
                throw new ArgumentException(
                    $"{fieldName ?? "Field"} text is too long (max {padToBytes} bytes UTF-8)");

            if (padToBytes > 0)
            {
                var buffer = new byte[padToBytes];
                Array.Copy(bytes, buffer, bytes.Length);
                return Convert.ToHexString(buffer);
            }

            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// Decodes a hex-encoded string back to its human-readable UTF-8 representation.
        /// Trailing null bytes (0x00) are trimmed.
        /// </summary>
        /// <param name="hex">Hex-encoded string.</param>
        /// <returns>Decoded UTF-8 string, or null if input is null/empty.</returns>
        public static string FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;

            var bytes = Convert.FromHexString(hex);

            int len = Array.IndexOf(bytes, (byte)0x00);
            if (len < 0)
                len = bytes.Length;

            return Encoding.UTF8.GetString(bytes, 0, len);
        }

        /// <summary>
        /// Checks if a string contains only valid hexadecimal characters
        /// and has an even length (complete byte pairs).
        /// </summary>
        public static bool IsValidHex(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length % 2 != 0)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex =
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }

            return true;
        }
    }
}
