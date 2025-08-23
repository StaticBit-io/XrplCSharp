using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Reflection;

using Xrpl.Models.Transactions.Batches;
using Xrpl.Models.Utils;

namespace Xrpl.Models.Transactions;

public sealed class BatchSigner
{
    [JsonProperty("BatchSigner", Required = Required.Always)]
    public InnerSigner Value { get; set; } = new InnerSigner();

    public sealed class InnerSigner
    {
        [JsonProperty("Account", Required = Required.Always)]
        public string Account { get; set; } = string.Empty;

        [JsonProperty("SigningPubKey", NullValueHandling = NullValueHandling.Ignore)]
        public string? SigningPubKey { get; set; } = string.Empty;

        [JsonProperty("TxnSignature", NullValueHandling = NullValueHandling.Ignore)]
        public string? TxnSignature { get; set; }

        [JsonProperty("Signers", NullValueHandling = NullValueHandling.Ignore)]
        public List<Signer>? Signers { get; set; }
    }
}

//public sealed class BatchInnerTransaction
//{
//    //// Храним как JObject, чтобы поддержать ЛЮБОЙ тип транзакции проекта,
//    //// но при сборке/валидации проверки делаем программно.
//    [JsonExtensionData]
//    public IDictionary<string, JToken> Fields { get; set; } = new Dictionary<string, JToken>();

//    public string? GetTransactionType() =>
//        Fields.TryGetValue(key: "TransactionType", value: out var v) ? v.ToString() : null;

//    public void SetFlag(BatchGlobalFlags flag)
//    {
//        // Поле Flags может быть числом либо отсутствовать
//        uint flags = 0;
//        if (Fields.TryGetValue(key: "Flags", value: out var fv) && fv.Type != JTokenType.Null && fv.ToString() != "")
//        {
//            // xrpl.org использует беззнаковое число; сериализуем как int/uint.
//            flags = fv.Type switch
//            {
//                JTokenType.Integer => (uint)fv.Value<long>(),
//                JTokenType.String => uint.TryParse(s: fv.ToString(), result: out var u) ? u : 0u,
//                _ => 0u,
//            };
//        }

//        flags |= (uint)flag;
//        Fields["Flags"] = flags;
//    }

//    public void ForceField(string name, JToken value) => Fields[name] = value;

//    public void RemoveField(string name)
//    {
//        if (Fields.ContainsKey(name))
//        {
//            Fields.Remove(name);
//        }
//    }
//    public JObject ToJObject()
//    {
//        var o = new JObject();
//        foreach (var kv in Fields)
//            o[kv.Key] = kv.Value;
//        return o;
//    }
//}

public sealed class RawTransactionWrapper
{
    [JsonProperty("RawTransaction", Required = Required.Always)]
    public ITransactionCommon RawTransaction { get; set; }
}

public interface IBatch : ITransactionCommon
{
    new BatchFlags? Flags { get; set; }

    List<BatchSigner>? BatchSigners { get; set; }

    List<RawTransactionWrapper> RawTransactions { get; set; }

}

public sealed class Batch : TransactionCommon, IBatch
{
    public Batch() => TransactionType = TransactionType.Batch;

    // Допустимо — 0 или 1 режим (бит из BatchFlags) вместе с обычными глобальными флагами.
    [JsonProperty("Flags", NullValueHandling = NullValueHandling.Ignore)]
    public new BatchFlags? Flags { get; set; }

    [JsonProperty("BatchSigners", NullValueHandling = NullValueHandling.Ignore)]
    public List<BatchSigner>? BatchSigners { get; set; }

    [JsonProperty("RawTransactions", Required = Required.Always)]
    public List<RawTransactionWrapper> RawTransactions { get; set; } = new List<RawTransactionWrapper>();
}

public partial class Validation
{
    public static void Validate(Batch tx)
    {
        if (tx.TransactionType != TransactionType.Batch)
        {
            throw new ArgumentException("Batch: TransactionType must be 'Batch'.");
        }

        if (tx.RawTransactions == null || tx.RawTransactions.Count == 0)
        {
            throw new ArgumentException("Batch: RawTransactions is required and must be non-empty.");
        }

        if (tx.RawTransactions.Count > 8)
        {
            throw new ArgumentException("Batch: RawTransactions length must be <= 8.");
        }

        // Проверка внутренних
        for (var i = 0; i < tx.RawTransactions.Count; i++)
        {
            var wrapper = tx.RawTransactions[i] ?? throw new ArgumentException($"Batch: RawTransactions[{i}] is null.");
            var innerTx = wrapper.RawTransaction ??
                      throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction is null.");


            if (innerTx.TransactionType == TransactionType.Batch)
            {
                throw new ArgumentException(
                    $"Batch: RawTransactions[{i}] cannot be a Batch transaction (nesting is not allowed).");
            }

            // Требуем tfInnerBatchTxn
            if ((innerTx.Flags & (uint)BatchGlobalFlags.tfInnerBatchTxn) == 0)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] must contain the `tfInnerBatchTxn` flag.");
            }

            // Силовые ограничения на поля
            // Fee: "0" (строка) — если присутствует, проверим; если нет — можно не добавлять, но лучше нормализовать заранее билдером
            if (innerTx.Fee is { } feeToken && feeToken.Value != "0")
            {
                throw new ArgumentException(
                    $"Batch: RawTransactions[{i}].RawTransaction.Fee must be string \"0\" when present.");
            }

            // SigningPubKey: "" — если присутствует
            if (innerTx.SigningPublicKey is { } spkToken && spkToken != "")
            {
                throw new ArgumentException(
                    $"Batch: RawTransactions[{i}].RawTransaction.SigningPubKey must be empty string when present.");
            }

            // Запрещённые поля
            if (innerTx.TransactionSignature != null)
            {
                throw new ArgumentException(
                    $"Batch: RawTransactions[{i}].RawTransaction.TxnSignature is not allowed inside Batch.");
            }
            if (innerTx.Signers != null)
            {
                throw new ArgumentException(
                    $"Batch: RawTransactions[{i}].RawTransaction.Signers is not allowed inside Batch.");
            }
        }

        // BatchSigners — при наличии проверим минимум Account
        if (tx.BatchSigners != null)
        {
            for (var i = 0; i < tx.BatchSigners.Count; i++)
            {
                var wrapper = tx.BatchSigners[i] ?? throw new ArgumentException($"Batch: BatchSigners[{i}] is null.");
                var s = wrapper.Value ?? throw new ArgumentException($"Batch: BatchSigners[{i}].BatchSigner is null.");
                if (string.IsNullOrWhiteSpace(s.Account))
                {
                    throw new ArgumentException($"Batch: BatchSigners[{i}].Account is required.");
                }

                // SigningPubKey / TxnSignature / Signers — опциональны (как в xrpl.js)
            }
        }
    }
}