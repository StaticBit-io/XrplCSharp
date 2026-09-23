using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.BinaryCodec.Enums;
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
    public const uint UnknownPermissionValue = DelegatablePermissions.UnknownPermissionValue;

    private const string PermissionValuePropertyName = "PermissionValue";

    /// <summary>
    /// Maps a permission name to its numeric value.
    /// </summary>
    /// <param name="name">A granular permission name or a transaction type name.</param>
    /// <param name="value">The numeric value, when the name is known to this version.</param>
    /// <returns>True when the name was mapped.</returns>
    public static bool TryGetPermissionValue(string name, out uint value) =>
        DelegatablePermissions.TryGetValue(name, out value);

    /// <summary>
    /// Maps a numeric permission value back to the name rippled uses for it.
    /// </summary>
    /// <param name="value">The numeric permission value.</param>
    /// <param name="name">The permission name, when the value is known to this version.</param>
    /// <returns>True when the value was mapped.</returns>
    public static bool TryGetPermissionName(uint value, out string name) =>
        DelegatablePermissions.TryGetName(value, out name);

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

        // The name, matching what a node reports and what rippled's own getJson emits, so a
        // transaction read from a node and written back out reads the same. The codec accepts
        // either form and encodes both to the same bytes, so signing is unaffected.
        if (TryGetPermissionName(value.PermissionValue, out string name))
            writer.WriteStringValue(name);
        else if (value.PermissionValue == UnknownPermissionValue && !string.IsNullOrEmpty(value.PermissionValueName))
            writer.WriteStringValue(value.PermissionValueName);
        else
            writer.WriteNumberValue(value.PermissionValue);

        writer.WriteEndObject();
    }
}
