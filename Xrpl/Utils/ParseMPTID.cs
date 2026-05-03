using System;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/parseMPTokenIssuanceID.ts

namespace Xrpl.Utils
{
    /// <summary>
    /// Utilities for encoding and decoding the 192-bit MPTokenIssuanceID (XLS-33).
    /// Binary layout: Sequence (UInt32, big-endian, 4 bytes) || Issuer AccountID (20 bytes).
    /// Hex layout: 48 uppercase hex characters (8 for Sequence + 40 for AccountID).
    /// </summary>
    public static class ParseMPTID
    {
        private const int ExpectedLength = 48;
        private const int SequenceHexLength = 8;
        private const int AccountIdHexLength = 40;

        /// <summary>
        /// Parses a 48-hex MPTokenIssuanceID string into its Sequence and Issuer components.
        /// </summary>
        /// <param name="mptIssuanceId">Hex-encoded 192-bit identifier (case-insensitive).</param>
        /// <returns>Decoded <see cref="MPTokenIssuanceIdData"/>.</returns>
        /// <exception cref="XrplException">Thrown when the input is null, empty, or has wrong length.</exception>
        public static MPTokenIssuanceIdData ParseMPTokenIssuanceID(this string mptIssuanceId)
        {
            if (string.IsNullOrEmpty(mptIssuanceId))
            {
                throw new XrplException("MPTokenIssuanceID is null or empty.");
            }

            if (mptIssuanceId.Length != ExpectedLength)
            {
                throw new XrplException(
                    $"Attempting to parse an MPTokenIssuanceID with length {mptIssuanceId.Length}, " +
                    $"but expected a value with length {ExpectedLength}.");
            }

            uint sequence = Convert.ToUInt32(mptIssuanceId.Substring(0, SequenceHexLength), 16);
            string issuerHex = mptIssuanceId.Substring(SequenceHexLength, AccountIdHexLength);
            string issuer = XrplCodec.EncodeAccountID(issuerHex.FromHexToBytes());

            return new MPTokenIssuanceIdData(sequence, issuer);
        }

        /// <summary>
        /// Builds a 48-hex uppercase MPTokenIssuanceID from a transaction sequence and issuer r-address.
        /// </summary>
        /// <param name="sequence">Sequence (or Ticket) number of the MPTokenIssuanceCreate transaction.</param>
        /// <param name="issuer">Issuer classic address (r-address).</param>
        /// <returns>Uppercase 48-character hex string identical to rippled's <c>mpt_issuance_id</c>.</returns>
        /// <exception cref="XrplException">Thrown when <paramref name="issuer"/> is null or empty.</exception>
        public static string GenerateMPTokenIssuanceID(uint sequence, string issuer)
        {
            if (string.IsNullOrEmpty(issuer))
            {
                throw new XrplException("Issuer is null or empty.");
            }

            string sequenceHex = sequence.ToString("X8");
            byte[] issuerBytes = XrplCodec.DecodeAccountID(issuer);
            string issuerHex = BitConverter.ToString(issuerBytes).Replace("-", string.Empty);

            return $"{sequenceHex}{issuerHex}".ToUpperInvariant();
        }
    }
}
