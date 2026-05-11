using System.Text.Json.Serialization;

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
    /// A bit-flag value representing the specific permission granted.
    /// Each bit corresponds to a particular transaction type or capability.
    /// </summary>
    [JsonPropertyName("PermissionValue")]
    public uint PermissionValue { get; set; }
}
