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
    public override void Write(Utf8JsonWriter writer, NFToken value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("NFToken");

        // Remove this converter to avoid infinite recursion
        JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Remove(this);
        JsonSerializer.Serialize(writer, value, innerOptions);

        writer.WriteEndObject();
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
