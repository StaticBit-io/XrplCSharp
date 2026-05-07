
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters;


using Xrpl.Models.Methods;

public sealed class ServerFeaturesConverter : JsonConverter<ServerFeatures>
{
    private static readonly HashSet<string> MetaFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ledger_hash",
        "ledger_index",
        "validated",
        "features"
    };

    public override ServerFeatures Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        ServerFeatures response = new ServerFeatures
        {
            LedgerHash = root.TryGetProperty("ledger_hash", out JsonElement lh) ? lh.GetString() : null,
            LedgerIndex = root.TryGetProperty("ledger_index", out JsonElement li) ? li.GetUInt32() : 0,
            Validated = root.TryGetProperty("validated", out JsonElement v) && v.GetBoolean()
        };

        // ─────────────────────────────────────────────
        // Format #1: { "features": { HASH: { ... } } }
        // ─────────────────────────────────────────────
        if (root.TryGetProperty("features", out JsonElement featuresEl) &&
            featuresEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in featuresEl.EnumerateObject())
            {
                FeatureInfo info = JsonSerializer.Deserialize<FeatureInfo>(prop.Value.GetRawText(), options);
                if (info != null)
                    response.Features[prop.Name] = info;
            }

            return response;
        }

        // ─────────────────────────────────────────────
        // Format #2: { "HASH": { ... }, ledger_* }
        // ─────────────────────────────────────────────
        foreach (JsonProperty prop in root.EnumerateObject())
        {
            if (MetaFields.Contains(prop.Name))
                continue;

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                FeatureInfo info = JsonSerializer.Deserialize<FeatureInfo>(prop.Value.GetRawText(), options);
                if (info != null)
                    response.Features[prop.Name] = info;
            }
        }

        return response;
    }

    public override void Write(Utf8JsonWriter writer, ServerFeatures value, JsonSerializerOptions options)
        => throw new NotSupportedException("Serialization is not required.");
}
