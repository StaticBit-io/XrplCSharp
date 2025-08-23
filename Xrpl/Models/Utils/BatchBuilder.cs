using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.BinaryCodec.Enums;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Models.Transactions.Batches;

namespace Xrpl.Models.Utils;
public static class BatchBuilder
{
    /// <summary>
    /// Превращает список произвольных «обычных» транзакций (твоих C# моделей) во внутренние RawTransactions
    /// с нужными полями для Batch (Fee= "0", SigningPubKey = "", + tfInnerBatchTxn; без TxnSignature/Signers/LastLedgerSequence).
    /// </summary>
    public static Batch Build(string account, IEnumerable<ITransactionCommon> transactions, BatchFlags? mode = null, List<BatchSigner>? batchSigners = null)
    {
        if (transactions == null) throw new ArgumentNullException(nameof(transactions));

        var batch = new Batch
        {
            Account = account,
            RawTransactions = new(),
            BatchSigners = batchSigners
        };

        if (mode.HasValue)
            batch.Flags = mode.Value;
        var batchInnerTxs = transactions.Select(ToBatchTx);
        batch.RawTransactions.AddRange(batchInnerTxs);

        // Валидация как в xrpl.js
        Xrpl.Models.Transactions.Validation.Validate(batch);
        return batch;
    }

    public static RawTransactionWrapper ToBatchTx( this ITransactionCommon tx)
    {
        // 1) Запрещаем Batch внутри Batch
        if (tx.TransactionType == TransactionType.Batch)
            throw new ArgumentException("Nested Batch is not allowed.");

        // 2) Удаляем запрещённые/нестабильные для Batch поля
        tx.TransactionSignature = null;
        tx.Signers = null;
        tx.LastLedgerSequence = null;

        // 3) Принудительные поля
        tx.Fee = new Xrpl.Models.Common.Currency() { Value = "0" };
        tx.SigningPublicKey = ""; // пустая строка

        // 4) не Устанавливаем tfInnerBatchTxn, будет на подписании
        //uint flags = 0;
        //if (tx.Flags is { } fv)
        //{
        //    flags = fv;
        //}

        //flags |= (uint)BatchGlobalFlags.tfInnerBatchTxn;
        //tx.Flags = flags;
        return new RawTransactionWrapper
        {
            RawTransaction = tx
        };
    }

    /// <summary>
    /// Нормализует внутреннюю транзакцию по правилам XLS‑56:
    /// - добавляет флаг tfInnerBatchTxn;
    /// - удаляет TxnSignature, Signers, LastLedgerSequence;
    /// - принудительно выставляет Fee = "0" (строка), SigningPubKey = "".
    /// Возвращает новый JObject (исходник не меняется).
    /// </summary>
    public static JObject NormalizeInnerForBatch(JObject source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        // удаляем запрещённые поля
        source.Remove("TxnSignature");
        source.Remove("Signers");
        source.Remove("LastLedgerSequence");

        // принудительные поля
        source["Fee"] = "0";
        source["SigningPubKey"] = "";

        // устанавливаем tfInnerBatchTxn
        uint flags = 0;
        if (source.TryGetValue("Flags", out var fv) && fv.Type == JTokenType.Integer)
            flags = (uint)fv.Value<long>();
        else if (source.TryGetValue("Flags", out fv) && fv.Type == JTokenType.String && uint.TryParse(fv.ToString(), out var u))
            flags = u;

        flags |= (uint)BatchGlobalFlags.tfInnerBatchTxn;
        source["Flags"] = flags;

        return source;
    }

    public static JObject NormalizeInnerForBatch(ITransactionCommon tx)
    {
        var json = tx.ToJson();
        var inner = JObject.Parse(json);

        return NormalizeInnerForBatch(inner);
    }

    /// <summary>
    /// Вычисляет transactionID для нормализованной внутренней транзакции.
    /// Алгоритм: txid = SHA512Half( HashPrefix.TXN + STObject(tx).ToBytes() ).
    /// </summary>
    public static string ComputeInnerTxId(JObject normalizedInnerTx)
    {
        try
        {
            var st = Xrpl.BinaryCodec.Types.StObject.FromJson(normalizedInnerTx);
            var bytes = st.ToBytes();

            // Префикс для txID — TXN\0 (TransactionID)
            var prefix = Xrpl.BinaryCodec.Util.Bits.GetBytes((uint)Xrpl.BinaryCodec.Hashing.HashPrefix.TransactionId);
            var buf = new byte[prefix.Length + bytes.Length];
            Buffer.BlockCopy(prefix, 0, buf, 0, prefix.Length);
            Buffer.BlockCopy(bytes, 0, buf, prefix.Length, bytes.Length);

            var hash32 = Xrpl.BinaryCodec.Hashing.Sha512.Half(buf);
            return ToHex(hash32);
        }
        catch (Exception ex)
        {
            // Диагностика: покажем, какое поле ломает парсер
            throw new ValidationException("Failed to serialize inner tx for txID. Likely unknown field present. " +
                                          "Ensure there is no `SigningPublicKey`, `TransactionSignature`, `Meta`, and only XRPL fields remain.");
        }
    }
    private static string ToHex(byte[] data)
    {
        char[] c = new char[data.Length * 2];
        int b;
        for (int i = 0; i < data.Length; i++)
        {
            b = data[i] >> 4;
            c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
            b = data[i] & 0xF;
            c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
        }
        return new string(c).ToLowerInvariant();
    }
    /// <summary>
    /// Удобная обёртка: нормализует inner и считает txid.
    /// </summary>
    public static string ComputeInnerTxId(ITransactionCommon inner)
        => ComputeInnerTxId(NormalizeInnerForBatch(inner));

    /// <summary>
    /// Возвращает список txIDs для всех внутренних транзакций батча.
    /// Внутренние tx предварительно нормализуются.
    /// </summary>
    public static IReadOnlyList<string> ComputeInnerTxIds(Models.Transactions.Batch batch)
    {
        if (batch == null) throw new ArgumentNullException(nameof(batch));
        if (batch.RawTransactions == null || batch.RawTransactions.Count == 0)
            throw new ArgumentException("Batch.RawTransactions is empty.");

        return batch.RawTransactions
                    .Select(c => ComputeInnerTxId(c.RawTransaction))
                    .ToArray();
    }
}
