using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Reads and writes a <see cref="SignatureObject"/> in its wire shape: <c>SigningPubKey</c> and
    /// <c>TxnSignature</c>, or <c>Signers</c> with each entry wrapped in a <c>Signer</c> object.
    /// </summary>
    /// <remarks>
    /// Reading does not enforce either protocol form: a Batch inner transaction carries the field
    /// as an unsigned marker (an empty object, or an empty <c>SigningPubKey</c>), and that has to
    /// write back as it came. <see cref="SignatureObject.ValidateShape"/> checks a signed one.
    /// </remarks>
    public class SignatureObjectConverter : JsonConverter<SignatureObject>
    {
        /// <inheritdoc />
        public override SignatureObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"A signature object must be a JSON object, got {reader.TokenType}.");

            JsonObject json = JsonNode.Parse(ref reader)!.AsObject();
            return SignatureObject.ReadJsonObject(json);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, SignatureObject value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            value.ToJsonObject().WriteTo(writer);
        }
    }
}
