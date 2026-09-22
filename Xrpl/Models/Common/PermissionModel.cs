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
/// The entry is read and written by <see cref="PermissionValueConverter"/>, which resolves the
/// two properties below from the single PermissionValue field rippled sends.
/// </summary>
[JsonConverter(typeof(PermissionValueConverter))]
public class PermissionEntry
{
    /// <summary>
    /// The numeric value of the granted permission: transaction type code + 1
    /// for transaction-type permissions, or a granular permission value (65537+).
    /// <see cref="PermissionValueConverter.UnknownPermissionValue"/> when the node sent a name
    /// this version cannot map - read <see cref="PermissionValueName"/> in that case.
    /// </summary>
    public uint PermissionValue { get; set; }

    /// <summary>
    /// The permission name: the token the node sent, or the name resolved from
    /// <see cref="PermissionValue"/> when the node sent a number. Null only when a numeric value
    /// has no name in this version.
    /// </summary>
    public string PermissionValueName { get; set; }
}
