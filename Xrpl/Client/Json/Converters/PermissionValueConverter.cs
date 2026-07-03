using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.BinaryCodec.Types;

namespace Xrpl.Client.Json.Converters;

/// <summary>
/// Converts the PermissionValue field of a Delegate permission entry.
/// rippled returns the value as a name string in JSON responses
/// (a transaction type name or a granular permission name) but accepts
/// numeric values on input; the binary codec always uses the numeric form.
/// Transaction-type permissions have value = transaction type code + 1.
/// </summary>
public sealed class PermissionValueConverter : JsonConverter<uint>
{
    // Granular permission values per rippled include/xrpl/protocol/detail/permissions.macro
    private static readonly Dictionary<string, uint> GranularPermissions = new(StringComparer.Ordinal)
    {
        ["TrustlineAuthorize"] = 65537,
        ["TrustlineFreeze"] = 65538,
        ["TrustlineUnfreeze"] = 65539,
        ["AccountDomainSet"] = 65540,
        ["AccountEmailHashSet"] = 65541,
        ["AccountMessageKeySet"] = 65542,
        ["AccountTransferRateSet"] = 65543,
        ["AccountTickSizeSet"] = 65544,
        ["PaymentMint"] = 65545,
        ["PaymentBurn"] = 65546,
        ["MPTokenIssuanceLock"] = 65547,
        ["MPTokenIssuanceUnlock"] = 65548,
    };

    public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetUInt32();

        if (reader.TokenType == JsonTokenType.String)
        {
            string value = reader.GetString() ?? string.Empty;

            if (uint.TryParse(value, out uint numeric))
                return numeric;

            if (GranularPermissions.TryGetValue(value, out uint granular))
                return granular;

            if (TransactionType.Values.Has(value))
                return (uint)TransactionType.Values[value].Ordinal + 1;

            throw new JsonException($"Unknown PermissionValue '{value}'.");
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for PermissionValue.");
    }

    public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
