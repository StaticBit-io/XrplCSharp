using System.Text.Json.Nodes;

using System;
using System.Text.RegularExpressions;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/types/account-id.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Class defining how to encode and decode an AccountID<br/>
    /// The unique identifier for an account.
    /// </summary>
    public class AccountId : Hash160
    {
        static string HEX_REGEX = @"^[A-F0-9]{40}$";

        private string _encoded;

        private string Encoded
        {
            get => _encoded ??= XrplCodec.EncodeAccountID(Buffer);
            set => _encoded = value;
        }
        /// <summary>
        /// Defines how to construct an AccountID
        /// </summary>
        /// <param name="hash">value either an existing AccountID, a hex-string</param>
        /// <param name="encoded">hex encoded</param>
        public AccountId(byte[] hash, string encoded) : base(hash)
        {
            Encoded = encoded;
        }
        private AccountId() : base(new byte[0], allowEmpty: true)
        {
            Encoded = string.Empty;
        }
        /// <summary>
        /// Defines how to construct an AccountID
        /// </summary>
        /// <param name="v">value either an existing AccountID, a base58 r-Address</param>
        public AccountId(string v) :
            this(XrplCodec.DecodeAccountID(v), v) {}
        /// <summary>
        /// Defines how to construct an AccountID
        /// </summary>
        /// <param name="hash">a hex-string</param>
        public AccountId(byte[] hash) :
            this(hash, XrplCodec.EncodeAccountID(hash)) {}
        /// <summary> create instance from byte object </summary>
        /// <param name="value">string object</param>
        public static implicit operator AccountId(string value)
        {
            return new AccountId(value);
        }
        /// <summary> create instance from byte object </summary>
        /// <param name="value">object</param>
        public static implicit operator AccountId(uint value)
        {
            byte[] empty = new byte[20];
            var sourceArray = Bits.GetBytes(value);
            Array.Copy(sourceArray, 0, empty, 16, 4);
            return new AccountId(empty);
        }
        /// <summary> create instance from json object </summary>
        /// <param name="json">json object</param>
        public static implicit operator AccountId(JsonNode json)
        {
            return json == null ? null : FromJson(json);
        }
        /// <summary> create json object from this element </summary>
        /// <param name="v">AccountId object</param>
        public static implicit operator JsonNode(AccountId v)
        {
            return JsonValue.Create(v.ToString());
        }
        /// <summary> this object to string data </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return Encoded;
        }
        /// <summary> create instance from json object </summary>
        /// <param name="json">json object; an empty string yields <see cref="Empty"/></param>
        public new static AccountId FromJson(JsonNode json)
        {
            if (json is null)
            {
                return null;
            }
            string value = json.GetValue<string>();
            return value.Length == 0 ? Empty : new AccountId(value);
        }

        public static readonly AccountId Zero = 0;
        public static readonly AccountId Neutral = 1;

        /// <summary>
        /// A defaulted AccountID field, such as the Account of a <c>UNLModify</c> pseudo-transaction.
        /// rippled serializes it as a zero-length VL and renders it as <c>""</c> in JSON. It differs from
        /// <see cref="Zero"/>, the account of 20 zero bytes that <c>EnableAmendment</c> and <c>SetFee</c> carry.
        /// </summary>
        internal static readonly AccountId Empty = new AccountId();

        /// <summary> true when this is the zero-length <see cref="Empty"/> value </summary>
        internal bool IsEmpty => Buffer.Length == 0;

        /// <summary> create instance from an account id in hex, or from a classic address </summary>
        /// <param name="value">
        /// 40 <b>uppercase</b> hex characters, or a base58 classic address. The hex matcher is
        /// <c>^[A-F0-9]{40}$</c> with no ignore-case option, so lowercase hex is not recognised as
        /// hex and falls through to the address decoder.
        /// </param>
        public static AccountId FromValue(string value)
        {
            Regex rg = new Regex(HEX_REGEX);
            if (rg.Match(value).Success)
            {
                return new AccountId(value.FromHex());
            }
            return new AccountId(XrplCodec.DecodeAccountID(value));
        }

        /// <summary> create instance from binary parser</summary>
        /// <param name="parser">parser</param>
        /// <param name="hint">
        /// VL length of the field value, or <c>null</c> where the account is not VL-encoded
        /// (amount issuer, path step). A zero length yields <see cref="Empty"/>.
        /// </param>
        /// <exception cref="FormatException">the VL length is neither 0 nor 20</exception>
        public new static AccountId FromParser(BinaryParser parser, int? hint=null)
        {
            switch (hint)
            {
                case null:
                case 20:
                    return new AccountId(parser.Read(20));
                case 0:
                    return Empty;
                default:
                    throw new FormatException($"Invalid AccountID length: {hint}. Expected 0 or 20.");
            }
        }
    }
}