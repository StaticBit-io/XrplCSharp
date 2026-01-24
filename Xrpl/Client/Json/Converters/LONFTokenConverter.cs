using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace Xrpl.Client.Json.Converters;

/// <summary>
/// <see cref="BaseLedgerEntry"/> json converter
/// </summary>
public class LONFTokenConverter : JsonConverter
{

    /// <summary>
    /// Writes an <see cref="NFToken"/> to JSON, wrapping it in an NFToken property.
    /// Null fields are ignored based on the serializer settings.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The <see cref="NFToken"/> value to serialize.</param>
    /// <param name="serializer">The JSON serializer.</param>
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("NFToken");
        var jObject = JObject.FromObject(value, serializer);
        jObject.WriteTo(writer);
        writer.WriteEndObject();
    }


    /// <summary> read <see cref="BaseLedgerEntry"/>  from json object </summary>
    /// <param name="reader">json reader</param>
    /// <param name="objectType">object type</param>
    /// <param name="existingValue">object value</param>
    /// <param name="serializer">json serializer</param>
    /// <returns><see cref="NFToken"/> </returns>
    /// <exception cref="NotSupportedException">Cannot convert value</exception>
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject jObject = JObject.Load(reader);
        var value = jObject.GetValue("NFToken");
        var target = new NFToken();
        serializer.Populate(value.CreateReader(), target);

        return target;
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return typeof(NFToken).IsAssignableFrom(objectType);
    }

    public override bool CanWrite => false;
}