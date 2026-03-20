using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace Xrpl.Client.Json.Converters;

public class GatewayBalancesResponseConverter : JsonConverter<GatewayBalancesResponse>
{
    public override GatewayBalancesResponse ReadJson(
        JsonReader reader,
        Type objectType,
        GatewayBalancesResponse existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var obj = JObject.Load(reader);

        var account = obj["account"]?.ToString();

        var response = new GatewayBalancesResponse
        {
            Account = account,
            LedgerHash = obj["ledger_hash"]?.ToString(),
            LedgerIndex = obj["ledger_index"]?.ToObject<uint?>(serializer),
            Validated = obj["validated"]?.ToObject<bool>(serializer),

            Assets = ReadIssuerCurrencyList(obj["assets"], serializer),
            Balances = ReadIssuerCurrencyList(obj["balances"], serializer),
            FrozenBalances = ReadIssuerCurrencyList(obj["frozen_balances"], serializer),
            Obligations = ReadObligations(obj["obligations"], account)
        };

        return response;
    }

    public override void WriteJson(JsonWriter writer, GatewayBalancesResponse value, JsonSerializer serializer)
    {
        writer.WriteStartObject();

        if (!string.IsNullOrWhiteSpace(value.Account))
        {
            writer.WritePropertyName("account");
            writer.WriteValue(value.Account);
        }

        WriteIssuerCurrencyList(writer, serializer, "assets", value.Assets);
        WriteIssuerCurrencyList(writer, serializer, "balances", value.Balances);
        WriteIssuerCurrencyList(writer, serializer, "frozen_balances", value.FrozenBalances);
        WriteObligations(writer, "obligations", value.Obligations);

        if (!string.IsNullOrWhiteSpace(value.LedgerHash))
        {
            writer.WritePropertyName("ledger_hash");
            writer.WriteValue(value.LedgerHash);
        }

        if (value.LedgerIndex.HasValue)
        {
            writer.WritePropertyName("ledger_index");
            writer.WriteValue(value.LedgerIndex.Value);
        }

        if (value.Validated.HasValue)
        {
            writer.WritePropertyName("validated");
            writer.WriteValue(value.Validated.Value);
        }

        writer.WriteEndObject();
    }

    private static List<Currency> ReadIssuerCurrencyList(JToken token, JsonSerializer serializer)
    {
        var result = new List<Currency>();

        if (token is not JObject obj)
        {
            return result;
        }

        foreach (var property in obj.Properties())
        {
            var issuer = property.Name;

            if (property.Value.Type != JTokenType.Array)
            {
                continue;
            }

            var items = property.Value.ToObject<List<Currency>>(serializer) ?? new List<Currency>();

            foreach (var item in items.Where(x => x != null))
            {
                item.Issuer = issuer;
                result.Add(item);
            }
        }

        return result;
    }

    private static List<Currency> ReadObligations(JToken token, string account)
    {
        var result = new List<Currency>();

        if (token is not JObject obj)
        {
            return result;
        }

        foreach (var property in obj.Properties())
        {
            result.Add(new Currency
            {
                CurrencyCode = property.Name,
                Value = property.Value?.ToString(),
                Issuer = account
            });
        }

        return result;
    }

    private static void WriteIssuerCurrencyList(
        JsonWriter writer,
        JsonSerializer serializer,
        string propertyName,
        List<Currency> items)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();

        foreach (var group in items
                     .Where(x => !string.IsNullOrWhiteSpace(x.Issuer))
                     .GroupBy(x => x.Issuer))
        {
            writer.WritePropertyName(group.Key);
            serializer.Serialize(writer, group.ToList());
        }

        writer.WriteEndObject();
    }

    private static void WriteObligations(
        JsonWriter writer,
        string propertyName,
        List<Currency> items)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();

        foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.CurrencyCode)))
        {
            writer.WritePropertyName(item.CurrencyCode);
            writer.WriteValue(item.Value);
        }

        writer.WriteEndObject();
    }
}