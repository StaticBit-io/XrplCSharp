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
                JsonSerializer.Serialize(writer, jsonElement, options);
                return;
            }

            Type runtimeType = value.GetType();
            if (runtimeType.IsPrimitive || value is decimal)
            {
                throw new JsonException(
                    $"QueuedTransaction.tx must be a JSON string or object; got CLR type {runtimeType.Name}.");
            }

            if (value is Array)
            {
                throw new JsonException("QueuedTransaction.tx cannot be a JSON array.");
            }

            JsonSerializer.Serialize(writer, value, runtimeType, options);
        }
    }
}
