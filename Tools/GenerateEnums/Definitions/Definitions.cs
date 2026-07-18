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
        var fields = new Dictionary<string, FieldDef>();
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
        return Parse(payload);
    }

    private static JsonElement Require(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement e)
            ? e
            : throw new InvalidDataException($"definitions payload is missing '{name}'");

    private static Dictionary<string, int> ReadIntMap(JsonElement root, string name)
    {
        var map = new Dictionary<string, int>();
        foreach (JsonProperty prop in Require(root, name).EnumerateObject())
            map[prop.Name] = prop.Value.GetInt32();
        return map;
    }
}
