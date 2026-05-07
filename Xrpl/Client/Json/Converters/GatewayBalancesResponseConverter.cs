using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace Xrpl.Client.Json.Converters;

public class GatewayBalancesResponseConverter : JsonConverter<GatewayBalancesResponse>
{
    public override GatewayBalancesResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        string account = root.TryGetProperty("account", out JsonElement accEl) ? accEl.GetString() : null;

        GatewayBalancesResponse response = new GatewayBalancesResponse
        {
            Account = account,
            LedgerHash = root.TryGetProperty("ledger_hash", out JsonElement lhEl) ? lhEl.GetString() : null,
            LedgerIndex = root.TryGetProperty("ledger_index", out JsonElement liEl) ? liEl.GetUInt32() : null,
            Validated = root.TryGetProperty("validated", out JsonElement vEl) ? vEl.GetBoolean() : null,

            Assets = ReadIssuerCurrencyList(root, "assets", options),
            Balances = ReadIssuerCurrencyList(root, "balances", options),
            FrozenBalances = ReadIssuerCurrencyList(root, "frozen_balances", options),
            Obligations = ReadObligations(root, "obligations", account)
        };

        return response;
    }

    public override void Write(Utf8JsonWriter writer, GatewayBalancesResponse value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (!string.IsNullOrWhiteSpace(value.Account))
        {
            writer.WriteString("account", value.Account);
        }

        WriteIssuerCurrencyList(writer, options, "assets", value.Assets);
        WriteIssuerCurrencyList(writer, options, "balances", value.Balances);
        WriteIssuerCurrencyList(writer, options, "frozen_balances", value.FrozenBalances);
        WriteObligations(writer, "obligations", value.Obligations);

        if (!string.IsNullOrWhiteSpace(value.LedgerHash))
        {
            writer.WriteString("ledger_hash", value.LedgerHash);
        }

        if (value.LedgerIndex.HasValue)
        {
            writer.WriteNumber("ledger_index", value.LedgerIndex.Value);
        }

        if (value.Validated.HasValue)
        {
            writer.WriteBoolean("validated", value.Validated.Value);
        }

        writer.WriteEndObject();
    }

    private static List<Currency> ReadIssuerCurrencyList(JsonElement root, string propertyName, JsonSerializerOptions options)
    {
        List<Currency> result = new List<Currency>();

        if (!root.TryGetProperty(propertyName, out JsonElement token) || token.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in token.EnumerateObject())
        {
            string issuer = property.Name;

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            List<Currency> items = JsonSerializer.Deserialize<List<Currency>>(property.Value.GetRawText(), options) ?? new List<Currency>();

            foreach (Currency item in items.Where(x => x != null))
            {
                item.Issuer = issuer;
                result.Add(item);
            }
        }

        return result;
    }

    private static List<Currency> ReadObligations(JsonElement root, string propertyName, string account)
    {
        List<Currency> result = new List<Currency>();

        if (!root.TryGetProperty(propertyName, out JsonElement token) || token.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in token.EnumerateObject())
        {
            result.Add(new Currency
            {
                CurrencyCode = property.Name,
                Value = property.Value.ToString(),
                Issuer = account
            });
        }

        return result;
    }

    private static void WriteIssuerCurrencyList(
        Utf8JsonWriter writer,
        JsonSerializerOptions options,
        string propertyName,
        List<Currency> items)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();

        foreach (IGrouping<string, Currency> group in items
                     .Where(x => !string.IsNullOrWhiteSpace(x.Issuer))
                     .GroupBy(x => x.Issuer))
        {
            writer.WritePropertyName(group.Key);
            JsonSerializer.Serialize(writer, group.ToList(), options);
        }

        writer.WriteEndObject();
    }

    private static void WriteObligations(
        Utf8JsonWriter writer,
        string propertyName,
        List<Currency> items)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();

        foreach (Currency item in items.Where(x => !string.IsNullOrWhiteSpace(x.CurrencyCode)))
        {
            writer.WriteString(item.CurrencyCode, item.Value);
        }

        writer.WriteEndObject();
    }
}
