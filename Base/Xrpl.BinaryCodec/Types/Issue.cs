using Newtonsoft.Json.Linq;

using Xrpl.BinaryCodec.Binary;

// https://github.com/XRPLF/xrpl.js/blob/amm/packages/ripple-binary-codec/src/types/issue.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Represents an Issue (currency + optional issuer), similar to ripple-binary-codec/src/types/issue.ts
    /// </summary>
    public class Issue : ISerializedType
    {
        public readonly Currency Currency;
        public readonly AccountId Issuer;

        /// <summary>
        /// XRP Issue: only currency, no issuer.
        /// </summary>
        public Issue()
        {
            Currency = Currency.Xrp;
            Issuer = null;
        }

        /// <summary>
        /// IOU Issue: currency with specified issuer.
        /// </summary>
        public Issue(Currency currency, AccountId issuer)
        {
            Currency = currency ?? Currency.Xrp;
            Issuer = issuer;
        }

        /// <inheritdoc/>
        public void ToBytes(IBytesSink sink)
        {
            // Always write 20 bytes for the currency code
            Currency.ToBytes(sink);
            // For IOU (non-XRP), write an additional 20 bytes for the issuer
            if (!Currency.IsNative)
            {
                Issuer.ToBytes(sink);
            }
        }

        /// <inheritdoc/>
        public JToken ToJson()
        {
            if (Currency.IsNative)
            {
                // Return JSON { "currency": "XRP" }
                return new JObject
                {
                    ["currency"] = Currency.ToString()
                };
            }
            // Return JSON { "currency": "<code>", "issuer": "<address>" }
            return new JObject
            {
                ["currency"] = Currency,
                ["issuer"] = Issuer
            };
        }

        /// <summary>
        /// Deserialize from JSON, distinguishing XRP and IOU issues.
        /// </summary>
        public static Issue FromJson(JToken token)
        {
            if (token.Type != JTokenType.Object)
                throw new InvalidJsonException($"Issue must be a JSON object, got {token.Type}");

            var obj = (JObject)token;
            var currencyStr = obj.Value<string>("currency");
            if (currencyStr is null)
                throw new InvalidJsonException("Issue object must contain property 'currency'.");

            var currency = Currency.FromString(currencyStr);

            if (currency.IsNative)
            {
                // XRP case: only one property allowed
                if (obj.Count != 1)
                    throw new InvalidJsonException("XRP Issue object must contain only 'currency'.");
                return new Issue();
            }

            // IOU case: expect exactly two properties
            if (obj.Count != 2)
                throw new InvalidJsonException("Issued currency object must contain exactly 'currency' and 'issuer'.");

            var issuerStr = obj.Value<string>("issuer");
            if (issuerStr is null)
                throw new InvalidJsonException("Issue object must contain property 'issuer'.");

            var issuer = new AccountId(issuerStr);
            return new Issue(currency, issuer);
        }

        /// <summary>
        /// Read from binary parser: 20 bytes for currency, and for IOU an additional 20 bytes for issuer.
        /// </summary>
        public static Issue FromParser(BinaryParser parser, int? hint = null)
        {
            // First read 20-byte currency code
            var curr = Currency.FromParser(parser);
            if (curr.IsNative)
            {
                // XRP case
                return new Issue(curr, null);
            }
            // IOU case: read 20-byte issuer
            var issuer = AccountId.FromParser(parser);
            return new Issue(curr, issuer);
        }
    }
}
