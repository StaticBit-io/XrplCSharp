using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/types/amount.ts

namespace Xrpl.BinaryCodec.Types
{
    public class Amount : ISerializedType
    {
        public bool IsNative()
        {
            return (this.Value.ToBytes()[0] & 0x80) == 0;
        }

        public readonly AccountId Issuer;
        public readonly Currency Currency;
        //public bool IsNative => Currency.IsNative;
        public AmountValue Value;

        public const int MaximumIouPrecision = 16;

        public Amount(AmountValue value, Currency currency = null, AccountId issuer = null)
        {
            Currency = currency ?? Currency.Xrp;
            Issuer = issuer ?? (Currency.IsNative ? AccountId.Zero : AccountId.Neutral);
            Value = value;
        }

        public Amount(string v = "0", Currency c = null, AccountId i = null) :
                      this(AmountValue.FromString(v, c == null || c.IsNative), c, i)
        {
        }

        public Amount(decimal value, Currency currency, AccountId issuer = null) :
            this(value.ToString(CultureInfo.InvariantCulture), currency, issuer)
        {
        }

        public virtual void ToBytes(IBytesSink sink)
        {
            sink.Put(Value.ToBytes());
            if (!IsNative())
            {
                Currency.ToBytes(sink);
                Issuer.ToBytes(sink);
            }
        }

        public virtual JsonNode ToJson()
        {
            if (this.IsNative())
            {
                return JsonValue.Create(Value.ToString());
            }
            return new JsonObject
            {
                ["value"] = Value.ToString(),
                ["currency"] = (JsonNode)Currency,
                ["issuer"] = (JsonNode)Issuer,
            };
        }

        public static Amount FromJson(JsonNode token)
        {
            if (token == null)
                throw new InvalidJsonException("Cannot create Amount from null JSON.");

            JsonValueKind kind = token.GetValueKind();
            if (kind == JsonValueKind.String)
            {
                return new Amount(token.GetValue<string>());
            }
            else if (kind == JsonValueKind.Number)
            {
                try
                {
                    return token.GetValue<ulong>();
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
                {
                    throw new InvalidJsonException(
                        $"Cannot convert numeric JSON value to a valid XRP drops amount: {ex.Message}", ex);
                }
            }
            else if (token is JsonObject)
            {
                JsonNode mptIssuanceId = token["mpt_issuance_id"];
                if (mptIssuanceId != null)
                {
                    JsonNode mptValue = token["value"];
                    if (mptValue == null)
                        throw new InvalidJsonException("MPT Amount object must contain property `value`.");
                    if (!(mptIssuanceId is JsonValue idJv) || idJv.GetValueKind() != JsonValueKind.String)
                        throw new InvalidJsonException("MPT Amount property `mpt_issuance_id` must be a JSON string.");
                    if (!(mptValue is JsonValue valJv) || valJv.GetValueKind() != JsonValueKind.String)
                        throw new InvalidJsonException("MPT Amount property `value` must be a JSON string.");
                    if (token.AsObject().Count > 2)
                        throw new InvalidJsonException("MPT Amount object has too many properties.");
                    return new MptAmount(valJv.GetValue<string>(), idJv.GetValue<string>());
                }

                JsonNode currencyNodeForXrp = token["currency"];
                if (currencyNodeForXrp is JsonValue currencyJv
                    && currencyJv.GetValueKind() == JsonValueKind.String
                    && currencyJv.GetValue<string>() == "XRP")
                {
                    if (token.AsObject().Count != 2)
                        throw new InvalidJsonException("XRP Amount object must contain only `currency` and `value`.");

                    JsonNode valueTokenForXrp = token["value"];
                    if (valueTokenForXrp == null || valueTokenForXrp.GetValueKind() != JsonValueKind.String)
                        throw new InvalidJsonException("XRP Amount object must contain string property `value`.");
                    return new Amount(valueTokenForXrp.GetValue<string>());
                }
                JsonNode valueToken = token["value"];
                JsonNode currencyToken = token["currency"];
                JsonNode issuerToken = token["issuer"];

                if (valueToken == null)
                    throw new InvalidJsonException("Amount object must contain property `value`.");

                if (currencyToken == null)
                    throw new InvalidJsonException("Amount object must contain property `currency`.");

                if (issuerToken == null)
                    throw new InvalidJsonException("Amount object must contain property `issuer`.");

                if (token.AsObject().Count > 3)
                    throw new InvalidJsonException("Amount object has too many properties.");

                if (valueToken.GetValueKind() != JsonValueKind.String)
                    throw new InvalidJsonException("Property `value` must be string.");

                if (currencyToken.GetValueKind() != JsonValueKind.String)
                    throw new InvalidJsonException("Property `currency` must be string.");

                if (issuerToken.GetValueKind() != JsonValueKind.String)
                    throw new InvalidJsonException("Property `issuer` must be string.");

                return new Amount(valueToken.GetValue<string>(), currencyToken.GetValue<string>(), issuerToken.GetValue<string>());
            }
            else
            {
                throw new InvalidJsonException($"Can not create Amount from `{kind}`");
            }
        }

        public static implicit operator Amount(ulong a)
        {
            return new Amount(a.ToString("D"));
        }

        public static implicit operator Amount(string v)
        {
            return new Amount(v);
        }

        public static Amount FromParser(BinaryParser parser, int? hint = null)
        {
            var firstByte = parser.Peek();
            var isIou = (firstByte & 0x80) != 0;
            
            if (isIou)
            {
                var bytes = parser.Read(48);
                
                var valueBytes = new byte[8];
                System.Array.Copy(bytes, 0, valueBytes, 0, 8);
                var value = AmountValue.FromParser(new BufferParser(valueBytes));
                
                var currBytes = new byte[20];
                System.Array.Copy(bytes, 8, currBytes, 0, 20);
                var curr = Currency.FromParser(new BufferParser(currBytes));
                
                var issuerBytes = new byte[20];
                System.Array.Copy(bytes, 28, issuerBytes, 0, 20);
                var issuer = AccountId.FromParser(new BufferParser(issuerBytes));
                
                return new Amount(value, curr, issuer);
            }
            
            var isMpt = (firstByte & 0x20) != 0;
            if (isMpt)
            {
                var bytes = parser.Read(33);
                return MptAmount.FromBytes(bytes);
            }

            var bytes8 = parser.Read(8);
            var xrpValue = AmountValue.FromParser(new BufferParser(bytes8));
            return new Amount(xrpValue);
        }

        public decimal DecimalValue()
        {
            return decimal.Parse(Value.ToString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
        }

        public static Amount operator *(Amount a, decimal b)
        {
            return new Amount(
                (a.DecimalValue() * b).ToString(CultureInfo.InvariantCulture),
                              a.Currency, a.Issuer);
        }

        public static bool operator <(decimal a, Amount b)
        {
            return a < b.DecimalValue();
        }

        public static bool operator >(decimal a, Amount b)
        {
            return a > b.DecimalValue();
        }

        public Amount NewValue(decimal @decimal)
        {
            return new Amount(@decimal, Currency, Issuer);
        }
    }
}