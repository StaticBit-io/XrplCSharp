using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Xrpl.Wallet
{
    /// <summary>
    /// Extension methods for working with XRPL Secret Numbers format.
    /// Secret Numbers encode 16 bytes of entropy into 8 groups of 6 digits each,
    /// where 5 digits represent entropy and the 6th digit is a position-dependent checksum.
    /// This format is language-agnostic and allows real-time typo detection.
    /// See XLS-12d specification: https://github.com/XRPLF/XRPL-Standards/issues/15
    /// </summary>
    public static class XummExtension
    {
        /// <summary>
        /// Converts 16 bytes of entropy to an array of 8 secret number strings.
        /// Each string contains 6 digits: 5 digits of entropy + 1 checksum digit.
        /// </summary>
        /// <param name="entropy">16 bytes of entropy</param>
        /// <returns>Array of 8 secret number strings, each 6 digits long</returns>
        /// <exception cref="ArgumentException">When entropy is not exactly 16 bytes</exception>
        public static string[] EntropyToSecretNumbers(byte[] entropy)
        {
            if (entropy == null || entropy.Length != 16)
                throw new ArgumentException("Entropy must be exactly 16 bytes", nameof(entropy));

            var result = new string[8];
            for (int i = 0; i < 8; i++)
            {
                int value = (entropy[i * 2] << 8) | entropy[i * 2 + 1];
                int checksum = CalculateChecksum(i, value);
                result[i] = $"{value:D5}{checksum}";
            }
            return result;
        }

        /// <summary>
        /// Generates a cryptographically secure random set of 8 secret numbers.
        /// Uses 16 bytes of random entropy to create the secret.
        /// </summary>
        /// <returns>Array of 8 secret number strings, each 6 digits long</returns>
        public static string[] RandomSecretNumbers()
        {
            byte[] entropy = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(entropy);
            }
            return EntropyToSecretNumbers(entropy);
        }

        /// <summary>
        /// Calculates the checksum digit for a secret number at a given position.
        /// The checksum formula is: value * (position * 2 + 1) % 9
        /// This position-dependent checksum helps detect if numbers are entered in wrong order.
        /// </summary>
        /// <param name="position">Position of the number (0-7)</param>
        /// <param name="value">The 5-digit entropy value (0-65535)</param>
        /// <returns>Single checksum digit (0-8)</returns>
        public static int CalculateChecksum(int position, int value)
        {
            return value * (position * 2 + 1) % 9;
        }

        /// <summary>
        /// Converts an array of 8 secret number strings to 16 bytes of entropy.
        /// Validates checksums before conversion.
        /// </summary>
        /// <param name="numbers">Array of 8 secret number strings</param>
        /// <returns>16 bytes of entropy</returns>
        /// <exception cref="ArgumentException">When numbers are invalid or checksums don't match</exception>
        public static byte[] EntropyFromXummNumbers(string[] numbers)
        {
            if (numbers == null || numbers.Length != 8)
                throw new ArgumentException("Secret numbers must be an array of 8 strings", nameof(numbers));

            if (!CheckXummNumbers(numbers))
                throw new ArgumentException("Invalid secret numbers or checksum mismatch");

            var buffer = new byte[16];
            for (int i = 0; i < 8; i++)
            {
                int value = int.Parse(numbers[i].Substring(0, 5));
                buffer[i * 2] = (byte)(value >> 8);
                buffer[i * 2 + 1] = (byte)(value & 0xFF);
            }
            return buffer;
        }

        /// <summary>
        /// Parses a space-separated secret numbers string into an array.
        /// Accepts formats like "554872 394230 209376 323698 140250 387423 652803 258676"
        /// </summary>
        /// <param name="secretString">Space-separated secret numbers string</param>
        /// <returns>Array of 8 secret number strings</returns>
        /// <exception cref="ArgumentException">When the string format is invalid</exception>
        public static string[] ParseSecretString(string secretString)
        {
            if (string.IsNullOrWhiteSpace(secretString))
                throw new ArgumentException("Secret string cannot be empty", nameof(secretString));

            var parts = secretString.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
                throw new ArgumentException("Secret string must contain exactly 8 numbers separated by spaces");

            return parts;
        }

        /// <summary>
        /// Validates all 8 secret numbers including their checksums.
        /// </summary>
        /// <param name="numbers">Array of 8 secret number strings</param>
        /// <returns>True if all numbers and checksums are valid</returns>
        public static bool CheckXummNumbers(string[] numbers)
        {
            if (numbers == null || numbers.Length != 8)
                return false;
            return numbers.Select((n, i) => CheckXummSum(i, n)).All(c => c);
        }

        /// <summary>
        /// Validates a single secret number at a given position.
        /// Checks that the number is 6 digits and the checksum matches.
        /// </summary>
        /// <param name="position">Position of the number (0-7)</param>
        /// <param name="number">6-digit secret number string</param>
        /// <returns>True if the number format and checksum are valid</returns>
        public static bool CheckXummSum(int position, string number)
        {
            if (string.IsNullOrEmpty(number) || number.Length != 6)
                return false;

            if (!int.TryParse(number, out _))
                return false;

            var checkSum = int.Parse(number[5..]);
            var value = int.Parse(number[..5]);
            var expectedChecksum = CalculateChecksum(position, value);
            return expectedChecksum == checkSum;
        }
    }
}
