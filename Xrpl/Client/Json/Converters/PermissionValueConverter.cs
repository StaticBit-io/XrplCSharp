using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.BinaryCodec.Types;
using Xrpl.Models.Common;

namespace Xrpl.Client.Json.Converters;

/// <summary>
/// Converts a Delegate permission entry, whose only field is PermissionValue.
/// rippled returns that value as a name string in JSON responses
/// (a transaction type name or a granular permission name) but accepts
/// both forms on input; the binary codec always uses the numeric form.
/// Transaction-type permissions have value = transaction type code + 1.
/// <para>
/// The converter works on the whole entry rather than the value alone because a name this
/// version cannot map has no number to fall back to: it is kept in
/// <see cref="PermissionEntry.PermissionValueName"/> instead, which a converter bound to the
/// numeric property could not reach. A node gains permission names with every amendment, so
/// an unmapped name is ordinary data from an up-to-date node, not malformed input.
/// </para>
/// </summary>
public sealed class PermissionValueConverter : JsonConverter<PermissionEntry>
{
    /// <summary>
    /// The value stored for a name this version cannot map. rippled treats 0 as no permission
    /// at all, so it can never collide with a real one.
    /// </summary>
    public const uint UnknownPermissionValue = 0;

    private const string PermissionValuePropertyName = "PermissionValue";

    // Built from the enum rather than restated here: one table, and a caller can name a
    // permission as GranularPermission.TrustlineAuthorize instead of 65537.
    private static readonly Dictionary<string, uint> GranularPermissions = BuildGranularPermissions();

    private static Dictionary<string, uint> BuildGranularPermissions()
    {
        Dictionary<string, uint> permissions = new(StringComparer.Ordinal);

        // Spelled in full rather than imported: Xrpl.Models also holds a TransactionType enum,
        // which would collide with the codec's TransactionType class used below.
        foreach (Models.GranularPermission permission in Enum.GetValues<Models.GranularPermission>())
            permissions[permission.ToString()] = (uint)permission;

        return permissions;
    }

    private static readonly Lazy<Dictionary<uint, string>> PermissionNames = new(BuildPermissionNames);

    private static Dictionary<uint, string> BuildPermissionNames()
    {
        Dictionary<uint, string> names = new Dictionary<uint, string>();

        foreach (KeyValuePair<string, uint> granular in GranularPermissions)
            names[granular.Value] = granular.Key;

        foreach (TransactionType transactionType in TransactionType.Values)
        {
            // Invalid is defined with ordinal -1 and has no permission value.
            if (transactionType.Ordinal < 0)
                continue;

            names[(uint)transactionType.Ordinal + 1] = transactionType.Name;
        }

        return names;
    }

    /// <summary>
    /// Maps a permission name to its numeric value.
    /// </summary>
    /// <param name="name">A granular permission name or a transaction type name.</param>
    /// <param name="value">The numeric value, when the name is known to this version.</param>
    /// <returns>True when the name was mapped.</returns>
    public static bool TryGetPermissionValue(string name, out uint value)
    {
        if (name is not null)
        {
            if (GranularPermissions.TryGetValue(name, out value))
                return true;

            if (TransactionType.Values.Has(name))
            {
                value = (uint)TransactionType.Values[name].Ordinal + 1;
                return true;
            }
        }

        value = UnknownPermissionValue;
        return false;
    }

    /// <summary>
    /// Maps a numeric permission value back to the name rippled uses for it.
    /// </summary>
    /// <param name="value">The numeric permission value.</param>
    /// <param name="name">The permission name, when the value is known to this version.</param>
    /// <returns>True when the value was mapped.</returns>
    public static bool TryGetPermissionName(uint value, out string name)
    {
        if (value == UnknownPermissionValue)
        {
            name = null;
            return false;
        }

        return PermissionNames.Value.TryGetValue(value, out name);
    }

    /// <inheritdoc />
    public override PermissionEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Unexpected token {reader.TokenType} for a Permission entry.");

        StringComparison comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        PermissionEntry entry = new PermissionEntry();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return entry;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Unexpected token {reader.TokenType} in a Permission entry.");

            string propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, PermissionValuePropertyName, comparison))
                ReadPermissionValue(ref reader, entry);
            else
                reader.Skip();
        }

        throw new JsonException("Unexpected end of JSON in a Permission entry.");
    }

    private static void ReadPermissionValue(ref Utf8JsonReader reader, PermissionEntry entry)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                entry.PermissionValue = reader.GetUInt32();
                entry.PermissionValueName = TryGetPermissionName(entry.PermissionValue, out string numberName)
                    ? numberName
                    : null;
                return;

            case JsonTokenType.String:
                string token = reader.GetString() ?? string.Empty;

                if (uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out uint numeric))
                {
                    entry.PermissionValue = numeric;
                    entry.PermissionValueName = TryGetPermissionName(numeric, out string parsedName)
                        ? parsedName
                        : null;
                    return;
                }

                entry.PermissionValue = TryGetPermissionValue(token, out uint mapped)
                    ? mapped
                    : UnknownPermissionValue;
                entry.PermissionValueName = token;
                return;

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for PermissionValue.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PermissionEntry value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(PermissionValuePropertyName);

        // The number is what the binary codec reads, so it stays the default form. A name this
        // version could not map has no number to write, and rippled accepts the name itself.
        if (value.PermissionValue != UnknownPermissionValue || string.IsNullOrEmpty(value.PermissionValueName))
            writer.WriteNumberValue(value.PermissionValue);
        else
            writer.WriteStringValue(value.PermissionValueName);

        writer.WriteEndObject();
    }
}
