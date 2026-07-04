using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Common;

/// <summary>
/// A wrapper for a single permission entry in a Permissions array.
/// </summary>
public class PermissionWrapper
{
    /// <summary>
    /// The permission entry.
    /// </summary>
    [JsonPropertyName("Permission")]
    public PermissionEntry Permission { get; set; }
}

/// <summary>
/// Represents a single permission granted to a delegate account.
/// </summary>
public class PermissionEntry
{
    /// <summary>
    /// The numeric value of the granted permission: transaction type code + 1
    /// for transaction-type permissions, or a granular permission value (65537+).
    /// rippled returns a name string in JSON responses; the converter maps it back.
    /// </summary>
    [JsonPropertyName("PermissionValue")]
    [JsonConverter(typeof(PermissionValueConverter))]
    public uint PermissionValue { get; set; }
}
