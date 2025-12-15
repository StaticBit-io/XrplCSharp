using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Methods;


[JsonConverter(typeof(ServerFeaturesConverter))]
public class ServerFeatures
{
    /// <summary>
    /// Key = amendment hash
    /// </summary>
    public Dictionary<string, FeatureInfo> Features { get; set; } = new();
    /// <summary>
    /// The identifying hash of the ledger version that was closed.
    /// </summary>
    [JsonProperty("ledger_hash")]
    public string LedgerHash { get; set; }
    /// <summary>
    /// The ledger index of the ledger that was closed.
    /// </summary>
    [JsonProperty("ledger_index")]
    public ulong LedgerIndex { get; set; }
    public bool Validated { get; set; }

    /// <summary>
    /// Returns features that are currently in voting state
    /// (not enabled yet, but voting-related fields are present).
    /// </summary>
    public Dictionary<string, FeatureInfo> GetVoting()
        => Features
            .Where(kv => kv.Value.IsVoting)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    /// <summary>
    /// Returns features that have reached quorum
    /// (Count &gt;= Threshold, when both are present).
    /// </summary>
    public Dictionary<string, FeatureInfo> GetWithQuorum()
        => Features
            .Where(kv => kv.Value.HasQuorum)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Returns features that are eligible to be enabled:
    /// - not enabled
    /// - supported by the node
    /// - not vetoed
    /// - quorum reached (if voting data is available)
    /// </summary>
    public Dictionary<string, FeatureInfo> GetCanBeEnabled()
        => Features
            .Where(kv => kv.Value.CanBeEnabled)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    /// <summary>
    /// Returns features that are already activated (enabled in the ledger).
    /// </summary>
    public Dictionary<string, FeatureInfo> GetActivated()
        => Features
            .Where(kv => kv.Value.Enabled)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    public KeyValuePair<string, FeatureInfo>? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return Features
            .FirstOrDefault(kv =>
                kv.Value.Name != null &&
                string.Equals(kv.Value.Name, name, StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>
    /// Returns features whose name contains the given substring (case-insensitive).
    /// Useful for search boxes and fuzzy matching in UI.
    /// </summary>
    public Dictionary<string, FeatureInfo> GetByNameContains(string part)
    {
        if (string.IsNullOrWhiteSpace(part))
            return new Dictionary<string, FeatureInfo>();

        return Features
            .Where(kv =>
                kv.Value.Name != null &&
                kv.Value.Name.Contains(part, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Returns features supported by the connected node.
    /// </summary>
    public Dictionary<string, FeatureInfo> GetSupported()
        => Features
            .Where(kv => kv.Value.Supported)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Returns features that are vetoed.
    /// Handles both "vetoed": true and "vetoed": "Reason".
    /// </summary>
    public Dictionary<string, FeatureInfo> GetVetoed()
        => Features
            .Where(kv => kv.Value.IsVetoed)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Returns features that are marked as obsolete ("vetoed": "Obsolete").
    /// This is a strict subset of vetoed features.
    /// </summary>
    public Dictionary<string, FeatureInfo> GetObsolete()
        => Features
            .Where(kv =>
                kv.Value.VetoedReason != null &&
                string.Equals(kv.Value.VetoedReason, "Obsolete", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
}

/// <summary>
/// Describes a single XRPL amendment (feature).
/// 
/// Depending on rippled version and node mode (validator / non-validator),
/// the server may return either a minimal or an extended set of fields.
/// This model is designed to be forward-compatible.
/// </summary>
public sealed class FeatureInfo
{
    /// <summary>
    /// (May be omitted) The human-readable name for this amendment, if known.
    /// </summary>
    [JsonProperty("name")]
    public string? Name { get; set; }
    /// <summary>
    /// Whether this amendment is currently enabled in the latest ledger.
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }
    /// <summary>
    /// Whether the server knows how to apply this amendment.<br/>
    /// If this field is set to false (the server does not know how to apply this amendment) and enabled is set to true (this amendment is enabled in the latest ledger),
    /// this amendment may cause your server to be amendment blocked.
    /// </summary>
    [JsonProperty("supported")]
    public bool Supported { get; set; }

    /// <summary>
    /// Current number of validations or votes for this amendment.
    /// Present only in extended responses.
    /// </summary>    [JsonProperty("count")]
    public int? Count { get; set; }
    /// <summary>
    /// Required number of validations for this amendment to pass voting.
    /// Present only in extended responses.
    /// </summary>
    [JsonProperty("threshold")]
    public int? Threshold { get; set; }

    /// <summary>
    /// Total number of validator validations observed for this amendment.
    /// May differ from <see cref="Count"/> depending on rippled version.
    /// </summary>
    [JsonProperty("validations")]
    public int? Validations { get; set; }
    /// <summary>
    /// Raw "vetoed" value as returned by rippled.
    /// Can be: boolean (true/false) or string reason (e.g. "Obsolete").
    /// </summary>
    [JsonProperty("vetoed")]
    public JToken? VetoedRaw { get; set; }

    /// <summary>
    /// Normalized flag: amendment is vetoed (regardless of whether the server returned bool or string).
    /// </summary>
    [JsonIgnore]
    public bool IsVetoed
        => VetoedRaw switch
        {
            null => false,
            { Type: JTokenType.Boolean } => VetoedRaw.Value<bool>(),
            { Type: JTokenType.String } => !string.IsNullOrWhiteSpace(VetoedRaw.Value<string>()),
            _ => true // be conservative if an unknown type appears
        };

    /// <summary>
    /// Normalized veto reason when server returns a string (e.g. "Obsolete").
    /// Returns null if server only returned a boolean.
    /// </summary>
    [JsonIgnore]
    public string? VetoedReason
        => VetoedRaw?.Type == JTokenType.String ? VetoedRaw.Value<string>() : null;

    /// <summary>
    /// Indicates that the server returned voting-related fields for this amendment.
    /// Typically present when the amendment is not enabled yet (or was tracked historically).
    /// </summary>
    [JsonIgnore]
    public bool IsVoting
        => !Enabled && (Count.HasValue || Threshold.HasValue || Validations.HasValue);

    /// <summary>
    /// Whether the amendment meets the known quorum/threshold condition (when both values are present).
    /// Returns false if data is not present.
    /// </summary>
    [JsonIgnore]
    public bool HasQuorum
        => Count.HasValue && Threshold.HasValue && Count.Value >= Threshold.Value;

    /// <summary>
    /// A stricter helper: amendment is not enabled, not vetoed, node supports it,
    /// and (if voting data exists) it has quorum.
    /// 
    /// Note: If quorum data is absent, this returns true as long as other conditions hold.
    /// </summary>
    [JsonIgnore]
    public bool CanBeEnabled
        => !Enabled
           && Supported
           && !IsVetoed
           && (!Threshold.HasValue || HasQuorum);

    public override string ToString() => $"{Name} Enabled: {Enabled}, Supported: {Supported}";
}

public class ServerFeaturesRequest : BaseRequest
{
    public ServerFeaturesRequest()
    {
        Command = "feature";
    }
    /// <summary>
    /// (Optional) The unique ID of an amendment, as hexadecimal; or the short name of the amendment.<br/>
    /// If provided, limits the response to one amendment. Otherwise, the response lists all amendments.
    /// </summary>
    public string? Feature { get; set; }
}

