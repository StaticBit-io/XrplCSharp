using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> Hash Or Transaction json converter </summary>
    public class TransactionOrHashConverter : JsonConverter<HashOrTransaction>
    {
        /// <summary>
        /// Writes a <see cref="HashOrTransaction"/> to JSON.
        /// If the transaction hash is set, writes just the hash string.
        /// Otherwise, writes the full transaction object.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, HashOrTransaction value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (!string.IsNullOrEmpty(value.TransactionHash))
            {
                writer.WriteStringValue(value.TransactionHash);
            }
            else if (value.Transaction != null)
            {
                JsonSerializer.Serialize(writer, value.Transaction, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        /// <summary> read  <see cref="HashOrTransaction"/>  from json object </summary>
        public override HashOrTransaction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            HashOrTransaction hashOrTransaction = new HashOrTransaction();

            if (reader.TokenType == JsonTokenType.String)
            {
                hashOrTransaction.TransactionHash = reader.GetString();
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                hashOrTransaction.Transaction = JsonSerializer.Deserialize<LedgerTransaction>(ref reader, options);
            }

            return hashOrTransaction;
        }

        public override bool CanConvert(Type typeToConvert) => typeof(HashOrTransaction).IsAssignableFrom(typeToConvert);
    }
}
