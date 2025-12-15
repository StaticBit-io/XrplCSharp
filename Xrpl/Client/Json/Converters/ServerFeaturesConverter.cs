
using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    public override ServerFeatures ReadJson(
        JsonReader reader,
        Type objectType,
        ServerFeatures? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        var root = JObject.Load(reader);

        var response = new ServerFeatures
        {
            LedgerHash = root.Value<string>("ledger_hash"),
            LedgerIndex = root.Value<uint?>("ledger_index") ?? 0,
            Validated = root.Value<bool?>("validated") ?? false
        };

        // ─────────────────────────────────────────────
        // Format №1: { "features": { HASH: { ... } } }
        // ─────────────────────────────────────────────
        if (root.TryGetValue("features", out var featuresToken) &&
            featuresToken is JObject featuresObj)
        {
            response.Features = featuresObj.ToObject<Dictionary<string, FeatureInfo>>(serializer)
                                ?? new Dictionary<string, FeatureInfo>();

            return response;
        }

        // ─────────────────────────────────────────────
        // Format №2: { "HASH": { ... }, ledger_* }
        // ─────────────────────────────────────────────
        foreach (var prop in root.Properties())
        {
            if (MetaFields.Contains(prop.Name))
                continue;

            if (prop.Value is JObject featureObj)
            {
                var info = featureObj.ToObject<FeatureInfo>(serializer);
                if (info != null)
                    response.Features[prop.Name] = info;
            }
        }

        return response;
    }

    public override void WriteJson(JsonWriter writer, ServerFeatures? value, JsonSerializer serializer)
        => throw new NotSupportedException("Serialization is not required.");

    public override bool CanWrite => false;
}
