using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Models.Common;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> currency json converter </summary>
    public class CurrencyConverter : JsonConverter<Currency>
    {
        /// <summary>
        /// write  <see cref="Currency"/>  to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"> <see cref="Currency"/> value</param>
        /// <param name="options">json serializer options</param>
        /// <exception cref="NotSupportedException">Cannot write this object type</exception>
        public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (!string.IsNullOrEmpty(value.MPTokenIssuanceID))
            {
                writer.WriteStartObject();
                writer.WriteString("mpt_issuance_id", value.MPTokenIssuanceID);
                writer.WriteString("value", value.Value);
                writer.WriteEndObject();
            }
            else if (value.CurrencyCode == "XRP" || string.IsNullOrEmpty(value.CurrencyCode))
            {
                writer.WriteStringValue(value.Value);
            }
            else
            {
                // IOU currency: serialize as object with currency/issuer/value fields
                writer.WriteStartObject();
                if (value.CurrencyCode != null)
                    writer.WriteString("currency", value.CurrencyCode);
                if (value.Issuer != null)
                    writer.WriteString("issuer", value.Issuer);
                if (value.Value != null)
                    writer.WriteString("value", value.Value);
                writer.WriteEndObject();
            }
        }

        /// <summary> read  <see cref="Currency"/>  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="typeToConvert">target type</param>
        /// <param name="options">json serializer options</param>
        /// <returns><see cref="Currency"/></returns>
        /// <exception cref="NotSupportedException">Cannot convert value</exception>
        public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return new Currency
                    {
                        CurrencyCode = "XRP",
                        Value = reader.GetString()
                    };
                case JsonTokenType.Number:
                    return new Currency
                    {
                        CurrencyCode = "XRP",
                        Value = reader.GetInt64().ToString()
                    };
                case JsonTokenType.StartObject:
                    return ReadCurrencyObject(ref reader);
                default:
                    throw new JsonException("Cannot convert value to Currency");
            }
        }

        private static Currency ReadCurrencyObject(ref Utf8JsonReader reader)
        {
            string mptIssuanceId = null;
            string currencyCode = null;
            string issuer = null;
            string value = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                string propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "mpt_issuance_id":
                        mptIssuanceId = reader.GetString();
                        break;
                    case "currency":
                        currencyCode = reader.GetString();
                        break;
                    case "issuer":
                        issuer = reader.GetString();
                        break;
                    case "value":
                        value = reader.TokenType == JsonTokenType.Number
                            ? reader.GetDecimal().ToString()
                            : reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (mptIssuanceId != null)
            {
                return new Currency
                {
                    MPTokenIssuanceID = mptIssuanceId,
                    Value = value
                };
            }

            Currency currency = new Currency();
            if (currencyCode != null) currency.CurrencyCode = currencyCode;
            if (issuer != null) currency.Issuer = issuer;
            if (value != null) currency.Value = value;
            return currency;
        }
    }

    /// <summary> issued currency json converter </summary>
    public class IssuedCurrencyConverter : JsonConverter<Common.IssuedCurrency>
    {
        /// <summary>
        /// write  <see cref="Common.IssuedCurrency"/>  to json object
        /// </summary>
        public override void Write(Utf8JsonWriter writer, Common.IssuedCurrency value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (value.Currency == "XRP")
            {
                writer.WriteStartObject();
                writer.WriteString("currency", "XRP");
                writer.WriteEndObject();
            }
            else
            {
                // Let the default serializer handle it
                writer.WriteStartObject();
                writer.WriteString("currency", value.Currency);
                if (value.Issuer != null)
                    writer.WriteString("issuer", value.Issuer);
                writer.WriteEndObject();
            }
        }

        /// <summary> read  <see cref="Common.IssuedCurrency"/>  from json object </summary>
        public override Common.IssuedCurrency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return new Common.IssuedCurrency { Currency = "XRP" };
                case JsonTokenType.StartObject:
                    {
                        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
                        JsonElement root = doc.RootElement;
                        Common.IssuedCurrency result = new Common.IssuedCurrency();
                        if (root.TryGetProperty("currency", out JsonElement curr))
                            result.Currency = curr.GetString();
                        if (root.TryGetProperty("issuer", out JsonElement iss))
                            result.Issuer = iss.GetString();
                        return result;
                    }
                default:
                    throw new JsonException("Cannot convert value to IssuedCurrency");
            }
        }
    }
}
