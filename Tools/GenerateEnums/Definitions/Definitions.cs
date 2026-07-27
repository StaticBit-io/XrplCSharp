using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GenerateEnums;

/// <summary>One FIELDS entry's metadata.</summary>
public sealed record FieldDef(string Type, int Nth, bool IsSigningField, bool IsSerialized, bool IsVLEncoded);

/// <summary>
/// Typed view of the five definitions sections shared by the local
/// definitions.json and a node's server_definitions response.
/// </summary>
public sealed record Definitions(
    IReadOnlyDictionary<string, FieldDef> Fields,
    IReadOnlyDictionary<string, int> Types,
    IReadOnlyDictionary<string, int> LedgerEntryTypes,
    IReadOnlyDictionary<string, int> TransactionResults,
    IReadOnlyDictionary<string, int> TransactionTypes)
{
    /// <summary>Parses a root element that directly holds the five sections.</summary>
    public static Definitions Parse(JsonElement root)
    {
        Dictionary<string, FieldDef> fields = new();
        foreach (JsonElement entry in Require(root, "FIELDS").EnumerateArray())
        {
            string name = entry[0].GetString()!;
            JsonElement p = entry[1];
            fields[name] = new FieldDef(
                p.GetProperty("type").GetString()!,
                p.GetProperty("nth").GetInt32(),
                p.GetProperty("isSigningField").GetBoolean(),
                p.GetProperty("isSerialized").GetBoolean(),
                p.GetProperty("isVLEncoded").GetBoolean());
        }

        return new Definitions(
            fields,
            ReadIntMap(root, "TYPES"),
            ReadIntMap(root, "LEDGER_ENTRY_TYPES"),
            ReadIntMap(root, "TRANSACTION_RESULTS"),
            ReadIntMap(root, "TRANSACTION_TYPES"));
    }

    /// <summary>Parses a node response, unwrapping the "result" envelope.</summary>
    public static Definitions ParseResponse(JsonElement responseRoot)
    {
        JsonElement payload = responseRoot.TryGetProperty("result", out JsonElement result)
            ? result
            : responseRoot;

        ThrowIfNodeError(responseRoot);
        ThrowIfNodeError(payload);

        return Parse(payload);
    }

    private static void ThrowIfNodeError(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;
        bool isError =
            (element.TryGetProperty("status", out JsonElement status) &&
             status.ValueKind == JsonValueKind.String &&
             string.Equals(status.GetString(), "error", StringComparison.Ordinal)) ||
            element.TryGetProperty("error", out _);
        if (!isError)
            return;
        string code = element.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.String
            ? err.GetString()!
            : "error";
        string message = element.TryGetProperty("error_message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String
            ? msg.GetString()!
            : "(no error_message)";
        throw new InvalidDataException($"node returned an error: {code} — {message}");
    }

    private static JsonElement Require(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement e)
            ? e
            : throw new InvalidDataException($"definitions payload is missing '{name}'");

    private static Dictionary<string, int> ReadIntMap(JsonElement root, string name)
    {
        Dictionary<string, int> map = new();
        foreach (JsonProperty prop in Require(root, name).EnumerateObject())
            map[prop.Name] = prop.Value.GetInt32();
        return map;
    }
}
