using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Serializes <c>queued_transaction.tx</c> as either a transaction hash string or a JSON object
    /// (expanded transaction or <c>tx_blob</c> wrapper), matching rippled <c>ledger</c> queue responses.
    /// </summary>
    public sealed class QueuedTransactionTxConverter : JsonConverter<object>
    {
        /// <inheritdoc />
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.StartObject => JsonSerializer.Deserialize<JsonElement>(ref reader, options),
                _ => throw new JsonException(
                    $"QueuedTransaction.tx must be a JSON string (hash) or object; got {reader.TokenType}.")
            };
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (value is string s)
            {
                writer.WriteStringValue(s);
                return;
            }

            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind != JsonValueKind.Object && jsonElement.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException(
                        $"QueuedTransaction.tx must be a JSON string or object; got JsonElement of kind {jsonElement.ValueKind}.");
                }
                JsonSerializer.Serialize(writer, jsonElement, options);
                return;
            }

            Type runtimeType = value.GetType();
            using JsonDocument doc = JsonSerializer.SerializeToDocument(value, runtimeType, options);
            JsonValueKind kind = doc.RootElement.ValueKind;
            if (kind != JsonValueKind.Object && kind != JsonValueKind.String)
            {
                throw new JsonException(
                    $"QueuedTransaction.tx must be a JSON string or object; got JSON {kind} from CLR type {runtimeType.Name}.");
            }
            doc.RootElement.WriteTo(writer);
        }
    }
}
