using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;

using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> Hash Or Transaction json converter </summary>
    public class TransactionOrHashConverter : JsonConverter
    {
        /// <summary>
        /// Writes a <see cref="HashOrTransaction"/> to JSON.
        /// If the transaction hash is set, writes just the hash string.
        /// Otherwise, writes the full transaction object.
        /// </summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The <see cref="HashOrTransaction"/> value to serialize.</param>
        /// <param name="serializer">The JSON serializer.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var hashOrTransaction = (HashOrTransaction)value;
            if (!string.IsNullOrEmpty(hashOrTransaction.TransactionHash))
            {
                writer.WriteValue(hashOrTransaction.TransactionHash);
            }
            else if (hashOrTransaction.Transaction != null)
            {
                var jObject = JObject.FromObject(hashOrTransaction.Transaction, serializer);
                jObject.WriteTo(writer);
            }
            else
            {
                writer.WriteNull();
            }
        }

        /// <summary> read  <see cref="HashOrTransaction"/>  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="objectType">object type</param>
        /// <param name="existingValue">object value</param>
        /// <param name="serializer">json serializer</param>
        /// <returns><see cref="HashOrTransaction"/></returns>
        /// <exception cref="NotSupportedException">Cannot convert value</exception>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            HashOrTransaction hashOrTransaction = new HashOrTransaction();


            if (reader.TokenType == JsonToken.String)
            {
                hashOrTransaction.TransactionHash = reader.Value.ToString();
            }
            else
            {
                hashOrTransaction.Transaction = serializer.Deserialize<LedgerTransaction>(reader);
            }

            return hashOrTransaction;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return true;
        }
    }
}
