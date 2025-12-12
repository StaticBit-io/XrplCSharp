using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Enums;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace Xrpl.Models.Utils;
public static class BatchUtils
{
    /// <summary>
    /// Превращает список произвольных «обычных» транзакций (твоих C# моделей) во внутренние RawTransactions
    /// с нужными полями для Batch (Fee= "0", SigningPubKey = "", + tfInnerBatchTxn; без TxnSignature/Signers/LastLedgerSequence).
    /// </summary>
    public static Batch Build(string account, IEnumerable<ITransactionRequest> transactions, BatchFlags? mode = null, List<BatchSigner>? batchSigners = null)
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
        Xrpl.Models.Transactions.Validation.Validate(JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(batch.ToJson()));
        return batch;
    }

    public static RawTransactionWrapper ToBatchTx(this ITransactionRequest tx)
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
        if (tx.Flags != null)
        {
            tx.Flags |= (uint)XrplGlobalFlags.tfInnerBatchTxn;
        }
        else
        {
            tx.Flags = (uint)XrplGlobalFlags.tfInnerBatchTxn;
        }
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
    public static JObject NormalizeInnerForBatch(this JObject source)
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

        // устанавливаем tfInnerBatchTxn, если ещё не установлен
        uint tfInnerBatchTxn = (uint)XrplGlobalFlags.tfInnerBatchTxn;
        if ((flags & tfInnerBatchTxn) == 0)
        {
            flags |= tfInnerBatchTxn;
        }
        source["Flags"] = flags;

        return source;
    }

    //public static JObject NormalizeInnerForBatch(ITransactionCommon tx)
    //{
    //    var json = tx.ToJson();
    //    var inner = JObject.Parse(json);

    //    return NormalizeInnerForBatch(inner);
    //}

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

    public static BatchSignerAccounts GetBatchSignerAccounts(this Dictionary<string, dynamic> tx)
    {
        if (tx is null)
            throw new ArgumentNullException(nameof(tx));

        // 1) Root
        if (!tx.TryGetValue("Account", out var rootObj) || rootObj is null)
            throw new ValidationException("Batch transaction must have top-level 'Account'.");

        var root = rootObj.ToString();
        if (string.IsNullOrWhiteSpace(root))
            throw new ValidationException("Top-level Account must be non-empty.");

        // 2) RawTransactions
        if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions is null)
            throw new ValidationException("Batch transaction must have RawTransactions field.");

        var raws = rawTransactions switch
        {
            JArray ja => ja.ToObject<List<Dictionary<string, dynamic>>>()
                         ?? new List<Dictionary<string, dynamic>>(),
            IEnumerable ie => ie.Cast<object>()
                    .Select(o => o as Dictionary<string, dynamic>
                              ?? JObject.FromObject(o!).ToObject<Dictionary<string, dynamic>>()!)
                    .ToList(),
            _ => throw new ValidationException("RawTransactions must be array/collection.")
        };

        var rawAccounts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var wrapper in raws)
        {
            if (!wrapper.TryGetValue("RawTransaction", out var rawTxObj) || rawTxObj is null)
                throw new ValidationException("Each RawTransactions item must contain RawTransaction.");

            // convert to pure dictionary
            var rawTx = rawTxObj as Dictionary<string, dynamic>
                        ?? JObject.FromObject(rawTxObj).ToObject<Dictionary<string, dynamic>>()!;

            wrapper["RawTransaction"] = rawTx;

            if (!rawTx.TryGetValue("Account", out object accObj) || accObj is null)
                throw new ValidationException("Each RawTransaction must contain Account.");

            var acc = accObj.ToString();

            if (string.IsNullOrWhiteSpace(acc))
                throw new ValidationException("RawTransaction.Account cannot be empty.");

            // skip root if appears inside RawTransactions
            if (acc.Equals(root, StringComparison.OrdinalIgnoreCase))
                continue;

            // distinct
            if (seen.Add(acc))
                rawAccounts.Add(acc);
        }

        return new BatchSignerAccounts
        {
            Root = root,
            Raw = rawAccounts
        };
    }
}

public sealed class BatchSignerAccounts
{
    public string Root { get; init; }

    public List<string> Raw { get; init; } = new List<string>();
}
public sealed class BatchSignStatus
{
    /// <summary>Root-аккаунт батча (поле Account верхнего уровня).</summary>
    public string Root { get; init; }

    /// <summary>Список всех уникальных аккаунтов-инициаторов внутренних транзакций (RawTransactions.RawTransaction.Account).</summary>
    public IReadOnlyList<string> InnerRequired { get; init; } = Array.Empty<string>();

    /// <summary>Аккаунты из InnerRequired, которые уже имеют подписи в BatchSigners.</summary>
    public IReadOnlyList<string> InnerSigned { get; init; } = Array.Empty<string>();

    /// <summary>Аккаунты из InnerRequired, по которым ещё НЕТ подписи в BatchSigners.</summary>
    public IReadOnlyList<string> InnerMissing { get; init; } = Array.Empty<string>();

    /// <summary>Есть ли у корня одиночная подпись (TxnSignature + SigningPubKey).</summary>
    public bool RootSignedSingle { get; init; }

    /// <summary>Есть ли у корня XRPL-мультиподпись (Signers[]).</summary>
    public bool RootSignedMulti { get; init; }

    /// <summary>Все ли внутренние аккаунты подписали батч.</summary>
    public bool AllInnerSigned => InnerMissing.Count == 0;

    /// <summary>Есть ли хоть какая-то подпись корня (single или multi).</summary>
    public bool IsRootSigned => RootSignedSingle || RootSignedMulti;
}

public static class BatchSignStatusExtensions
{
    /// <summary>
    /// Строит статус подписи батч-транзакции из tx-словаря.
    /// </summary>
    public static BatchSignStatus GetBatchSignStatus(this Dictionary<string, dynamic> tx)
    {
        if (tx is null) throw new ArgumentNullException(nameof(tx));

        if (!tx.TryGetValue("TransactionType", out var ttObj) ||
            !string.Equals($"{ttObj}", "Batch", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("GetBatchSignStatus: TransactionType must be 'Batch'.");
        }

        // Используем уже существующую утилиту, чтобы получить Root + Raw-аккаунты
        var accs = tx.GetBatchSignerAccounts(); // Root + Raw (distinct) :contentReference[oaicite:0]{index=0}
        var root = accs.Root;
        var requiredInner = accs.Raw
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Преобразуем в JObject, чтобы удобнее читать BatchSigners
        var outer = JObject.FromObject(tx);

        var signedInner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BatchSigners: [{ BatchSigner: { Account, SigningPubKey/TxnSignature или Signers[] } }, ...]
        var batchSignersArray = outer["BatchSigners"] as JArray;
        if (batchSignersArray != null)
        {
            foreach (var wrapper in batchSignersArray.Children<JObject>())
            {
                var bs = wrapper["BatchSigner"] as JObject ?? wrapper;
                var acc = (string?)bs["Account"];
                if (string.IsNullOrWhiteSpace(acc))
                    continue;

                // Проверяем, есть ли реальная подпись
                bool hasSignature = false;

                var signersArr = bs["Signers"] as JArray;
                if (signersArr != null && signersArr.Count > 0)
                {
                    hasSignature = true;
                }
                else
                {
                    var spk = (string?)bs["SigningPubKey"];
                    var sig = (string?)bs["TxnSignature"];
                    if (!string.IsNullOrEmpty(spk) && !string.IsNullOrEmpty(sig))
                        hasSignature = true;
                }

                if (hasSignature)
                    signedInner.Add(acc);
            }
        }

        var innerMissing = requiredInner
            .Where(a => !signedInner.Contains(a))
            .ToList();

        // Смотрим подпись корня: либо single (TxnSignature + SigningPubKey),
        // либо мульти (Signers[]).
        bool rootSingle = false;
        bool rootMulti = false;

        if ((tx.TryGetValue("TxnSignature", out var tsObj) && tsObj != null) &&
            (tx.TryGetValue("SigningPubKey", out var spObj) && spObj != null && !string.IsNullOrWhiteSpace($"{spObj}")))
        {
            rootSingle = true;
        }

        if (tx.TryGetValue("Signers", out var signersObj) && signersObj != null)
        {
            var jt = signersObj is JToken t ? t : JToken.FromObject(signersObj);
            if (jt is JArray arr && arr.Count > 0)
                rootMulti = true;
        }

        return new BatchSignStatus
        {
            Root = root,
            InnerRequired = requiredInner,
            InnerSigned = signedInner.ToList(),
            InnerMissing = innerMissing,
            RootSignedSingle = rootSingle,
            RootSignedMulti = rootMulti
        };
    }

    /// <summary>
    /// Быстро проверить полноту подписи батча (по inner-аккаунтам и, опционально, по корню).
    /// </summary>
    public static bool IsFullySignedBatch(this Dictionary<string, dynamic> tx, bool requireRoot = false)
    {
        var st = tx.GetBatchSignStatus();
        return st.AllInnerSigned && (!requireRoot || st.IsRootSigned);
    }

    /// <summary>
    /// Удобный хелпер для статуса по SignatureResult.
    /// </summary>
    public static BatchSignStatus GetBatchSignStatus(this SignatureResult signed)
    {
        if (signed == null) throw new ArgumentNullException(nameof(signed));
        var dict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(
            XrplBinaryCodec.Decode(signed.TxBlob).ToString());
        return dict.GetBatchSignStatus();
    }

    /// <summary>
    /// Удобный хелпер для статуса по hex-blob (tx_blob).
    /// </summary>
    public static BatchSignStatus GetBatchSignStatus(this string txBlob)
    {
        if (string.IsNullOrWhiteSpace(txBlob))
            throw new ArgumentNullException(nameof(txBlob));

        var dict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(
            XrplBinaryCodec.Decode(txBlob).ToString());
        return dict.GetBatchSignStatus();
    }
}
