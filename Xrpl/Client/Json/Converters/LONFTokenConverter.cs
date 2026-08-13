using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace Xrpl.Client.Json.Converters;

/// <summary>
/// <see cref="BaseLedgerEntry"/> json converter
/// </summary>
public class LONFTokenConverter : JsonConverter<NFToken>
{

    /// <summary>
    /// Writes an <see cref="NFToken"/> to JSON, wrapping it in an NFToken property.
    /// Null fields are ignored based on the serializer settings.
    /// </summary>
    /// <remarks>
    /// The fields are written by hand rather than by re-entering the serializer. This converter is declared
    /// as a <see cref="JsonConverterAttribute"/> on <see cref="NFToken"/> itself, and a converter attached
    /// to a type outranks <see cref="JsonSerializerOptions.Converters"/>: dropping this converter from that
    /// list — the usual way out of a recursive Write — does not stop System.Text.Json from picking it up
    /// again for the same type, so Write called itself until the writer hit MaxDepth.
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, NFToken value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("NFToken");

        writer.WriteStartObject();
        WriteField(writer, "NFTokenID", value.NFTokenID, options);
        WriteField(writer, "URI", value.URI, options);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a single string field, honouring the ignore condition of the options in effect so that the
    /// output matches what the reflection-based serializer would have produced for the same property.
    /// </summary>
    private static void WriteField(Utf8JsonWriter writer, string propertyName, string value, JsonSerializerOptions options)
    {
        if (value != null)
        {
            writer.WriteString(propertyName, value);
            return;
        }

        if (options.DefaultIgnoreCondition is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault)
            return;

        writer.WriteNull(propertyName);
    }


    /// <summary> read <see cref="BaseLedgerEntry"/>  from json object </summary>
    /// <param name="reader">json reader</param>
    /// <param name="typeToConvert">target type</param>
    /// <param name="options">json serializer options</param>
    /// <returns><see cref="NFToken"/> </returns>
    public override NFToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        JsonElement target = root.TryGetProperty("NFToken", out JsonElement nfTokenEl)
            ? nfTokenEl
            : root;

        return new NFToken
        {
            NFTokenID = target.TryGetProperty("NFTokenID", out JsonElement idEl) ? idEl.GetString() : null,
            URI = target.TryGetProperty("URI", out JsonElement uriEl) ? uriEl.GetString() : null,
        };
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeof(NFToken).IsAssignableFrom(typeToConvert);
}
