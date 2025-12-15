using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xrpl.Models.Common;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> currency json converter </summary>
    public class CurrencyConverter : JsonConverter
    {
        /// <summary>
        /// write  <see cref="Currency"/>  to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"> <see cref="Currency"/> value</param>
        /// <param name="serializer">json serializer</param>
        /// <exception cref="NotSupportedException">Cannot write this object type</exception>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Currency currency)
            {
                if (!string.IsNullOrEmpty(currency.MPTokenIssuanceID))
                {
                    var mptObject = new JObject
                    {
                        ["mpt_issuance_id"] = currency.MPTokenIssuanceID,
                        ["value"] = currency.Value
                    };
                    mptObject.WriteTo(writer);
                }
                else if (currency.CurrencyCode == "XRP" || string.IsNullOrEmpty(currency.CurrencyCode))
                {
                    writer.WriteValue(currency.Value);
                }
                else
                {
                    JToken t = JToken.FromObject(currency);
                    t.WriteTo(writer);
                }
            }
            else
            {
                throw new NotSupportedException("Cannot write this object type");
            }
        }
        /// <summary> read  <see cref="Currency"/>  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="objectType">object type</param>
        /// <param name="existingValue">object value</param>
        /// <param name="serializer">json serializer</param>
        /// <returns><see cref="Currency"/></returns>
        /// <exception cref="NotSupportedException">Cannot convert value</exception>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    return null;
                case JsonToken.String:
                    return new Currency
                    {
                        CurrencyCode = "XRP",
                        Value = reader.Value?.ToString()
                    };
                case JsonToken.StartObject:
                    var jObject = JObject.Load(reader);
                    if (jObject.ContainsKey("mpt_issuance_id"))
                    {
                        return new Currency
                        {
                            MPTokenIssuanceID = jObject["mpt_issuance_id"]?.ToString(),
                            Value = jObject["value"]?.ToString()
                        };
                    }
                    return jObject.ToObject<Currency>();
                default:
                    throw new NotSupportedException("Cannot convert value " + objectType);
            }
        }
        /// <summary> Can convert object to currency </summary>
        /// <param name="objectType">object type</param>
        /// <returns>bool result</returns>
        public override bool CanConvert(Type objectType) => objectType == typeof(Currency);
    }
    /// <summary> currency json converter </summary>
    public class IssuedCurrencyConverter : JsonConverter
    {
        /// <summary>
        /// write  <see cref="Currency"/>  to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"> <see cref="Currency"/> value</param>
        /// <param name="serializer">json serializer</param>
        /// <exception cref="NotSupportedException">Cannot write this object type</exception>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Common.IssuedCurrency currency)
            {
                if (currency.Currency == "XRP")
                {
                    JToken t = JToken.FromObject(new Common.XRP());
                    t.WriteTo(writer);
                }
                else
                {
                    JToken t = JToken.FromObject(currency);
                    t.WriteTo(writer);
                }
            }
            else
            {
                throw new NotSupportedException("Cannot write this object type");
            }
        }
        /// <summary> read  <see cref="Currency"/>  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="objectType">object type</param>
        /// <param name="existingValue">object value</param>
        /// <param name="serializer">json serializer</param>
        /// <returns><see cref="Currency"/></returns>
        /// <exception cref="NotSupportedException">Cannot convert value</exception>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            return reader.TokenType switch
            {
                JsonToken.Null => null,
                JsonToken.String => new Common.IssuedCurrency()
                {
                    Currency = "XRP",
                },

                JsonToken.StartObject => serializer.Deserialize<Common.IssuedCurrency>(reader),
                _ => throw new NotSupportedException("Cannot convert value " + objectType)
            };
        }
        /// <summary> Can convert object to currency </summary>
        /// <param name="objectType">object type</param>
        /// <returns>bool result</returns>
        public override bool CanConvert(Type objectType) => objectType == typeof(Common.IssuedCurrency);
    }
}
