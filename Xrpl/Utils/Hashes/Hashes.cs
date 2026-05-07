using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/hashes/index.ts

namespace Xrpl.Utils.Hashes
{
    //todo double need check
    public static class Hashes
    {
        const int HEX = 16;
        const int BYTE_LENGTH = 4;
        const byte MASK = 0xff;
        public static string AddressToHex(this string address)
        {
            return XrplCodec.DecodeAccountID(address).ToHex();
        }

        public static string LedgerSpaceHex(this LedgerSpace name)
        {
            return ((int)name).ToString("X4");
        }

        public static string LedgerSpaceHex(string name)
        {
            var enums = Enum.GetValues(typeof(LedgerSpace)).Cast<LedgerSpace>().ToList();

            var res = enums.FirstOrDefault(f => f.ToString() == name).ToString();
            var val = Convert.ToString(res.ToCharArray(0, 1)[0], HEX);
            while (val.Length < 4)
                val = "0" + val;
            return val;
        }
        /// <summary>
        /// check currency code for HEX 
        /// </summary>
        /// <param name="code">currency code</param>
        /// <returns></returns>
        public static bool IsHexCurrencyCode(this string code) => Regex.IsMatch(code, @"[0-9a-fA-F]{40}", RegexOptions.IgnoreCase);

        /// <summary>
        /// checks and generates a token code for transmission to the network
        /// </summary>
        /// <param name="currency">Currency code</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If Currency code length more than 40 characters</exception>
        public static string CurrencyToHex(this string currency)
        {
            var cur_code = currency.Trim();
            if (cur_code.Length == 3)
                return cur_code;

            if (cur_code.IsHexCurrencyCode())
                return cur_code;

            cur_code = cur_code.ConvertStringToHex();

            if (cur_code.Length > 40)
                throw new XrplException("wrong currency code format");

            cur_code += new string('0', 40 - cur_code.Length);

            return cur_code;

        }
        /// <summary>
        ///  Hash the given binary transaction data with the single-signing prefix.<br/>
        /// See [Serialization Format](https://xrpl.org/serialization.html).
        /// </summary>
        /// <param name="txBlobHex">The binary transaction blob as a hexadecimal string.</param>
        /// <returns>The hash to sign.</returns>
        public static string HashTx(this string txBlobHex)
        {

            var prefix = HashPrefix.TRANSACTION_SIGN.ToString("X").ToUpper();
            return (prefix + txBlobHex).Sha512Half();
        }


        public static string HashPaymentChannel(string address, string dstAddress, int sequence)
        {
            return (LedgerSpace.Paychan.LedgerSpaceHex() + address.AddressToHex() + dstAddress.AddressToHex() +
                    sequence.ToString("X").PadLeft(BYTE_LENGTH * 2, '0')).Sha512Half();
        }

        public static string HashTX(this string txBlobHex)
        {
            string prefix = ((int)HashPrefix.TRANSACTION_SIGN).ToString("X").ToUpper();
            return (prefix + txBlobHex).Sha512Half();
        }

        public static string HashAccountRoot(this string address)
        {
            return (LedgerSpace.Account.LedgerSpaceHex() + address.AddressToHex()).Sha512Half();
        }

        public static string HashSignerListId(this string address)
        {
            return (LedgerSpace.SignerList.LedgerSpaceHex() + address.AddressToHex() + "00000000").Sha512Half();
        }

        public static string HashOfferId(string address, int sequence)
        {

            string hexPrefix = LedgerSpace.Offer.LedgerSpaceHex().PadLeft(2, '0');
            string hexSequence = sequence.ToString("X").PadLeft(8, '0');
            string prefix = "00" + hexPrefix;
            return (prefix + address.AddressToHex() + hexSequence).Sha512Half();
        }

        public static string HashTrustline(string address1, string address2, string currency)
        {
            string address1Hex = address1.AddressToHex();
            string address2Hex = address2.AddressToHex();

            bool swap = (BigInteger.Parse(address1Hex, NumberStyles.HexNumber)>(BigInteger.Parse(address2Hex, NumberStyles.HexNumber)));
            string lowAddressHex = swap ? address2Hex : address1Hex;
            string highAddressHex = swap ? address1Hex : address2Hex;

            string prefix = LedgerSpace.RippleState.LedgerSpaceHex();
            return (prefix + lowAddressHex + highAddressHex + currency.CurrencyToHex()).Sha512Half();
        }

        public static string HashEscrow(string address, int sequence)
        {
            return (LedgerSpace.Escrow.LedgerSpaceHex() + address.AddressToHex() + sequence.ToString("X").PadLeft(BYTE_LENGTH * 2, '0')).Sha512Half();
        }

        /// <summary>
        /// Compute the ledger object index (object ID) for an XLS-70 Credential ledger entry.
        /// Mirrors rippled's <c>keylet::credential(subject, issuer, credentialType)</c>:
        /// <c>sha512Half(uint16(LedgerNameSpace::CREDENTIAL) || subject(20) || issuer(20) || credentialType)</c>.
        /// </summary>
        /// <param name="subject">Classic XRPL address of the credential subject.</param>
        /// <param name="issuer">Classic XRPL address of the credential issuer.</param>
        /// <param name="credentialTypeHex">Credential type as an uppercase hexadecimal string (1..64 bytes -> 2..128 hex chars).</param>
        /// <returns>The 64-character (32-byte) hexadecimal object ID of the credential ledger entry.</returns>
        /// <exception cref="ArgumentException">Thrown when arguments are missing or malformed.</exception>
        public static string HashCredential(string subject, string issuer, string credentialTypeHex)
        {
            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("subject is required", nameof(subject));
            }

            if (string.IsNullOrEmpty(issuer))
            {
                throw new ArgumentException("issuer is required", nameof(issuer));
            }

            if (string.IsNullOrEmpty(credentialTypeHex))
            {
                throw new ArgumentException("credentialTypeHex is required", nameof(credentialTypeHex));
            }

            string normalized = credentialTypeHex.Trim().ToUpperInvariant();
            if (normalized.Length == 0 || normalized.Length % 2 != 0 || !Regex.IsMatch(normalized, "^[0-9A-F]+$"))
            {
                throw new ArgumentException("credentialTypeHex must be an even-length hexadecimal string", nameof(credentialTypeHex));
            }

            if (normalized.Length > 128)
            {
                throw new ArgumentException("credentialTypeHex cannot exceed 64 bytes (128 hex characters)", nameof(credentialTypeHex));
            }

            return (LedgerSpace.Credential.LedgerSpaceHex()
                    + subject.AddressToHex()
                    + issuer.AddressToHex()
                    + normalized).Sha512Half();
        }

    }
}

