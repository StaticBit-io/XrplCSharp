using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace Xrpl.Models.Utils;

public static class BatchNormalizer
{
    private const uint TF_INNER_BATCH_TXN = (uint)XrplGlobalFlags.tfInnerBatchTxn;

    /// <summary>
    /// Нормализует внутреннюю транзакцию по правилам XLS‑56:
    /// - добавляет флаг tfInnerBatchTxn;
    /// - удаляет TxnSignature, Signers, LastLedgerSequence;
    /// - принудительно выставляет Fee = "0" (строка), SigningPubKey = "".
    /// Возвращает новый JObject (исходник не меняется).
    /// </summary>
    public static JObject NormalizeInnerTransaction(this JObject source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        source.Remove("TxnSignature");
        source.Remove("Signers");
        source.Remove("LastLedgerSequence");

        source["Fee"] = "0";
        source["SigningPubKey"] = "";

        uint flags = 0;
        if (source.TryGetValue("Flags", out var fv))
        {
            if (fv.Type == JTokenType.Integer)
                flags = (uint)fv.Value<long>();
            else if (fv.Type == JTokenType.String && uint.TryParse(fv.ToString(), out var u))
                flags = u;
        }

        if ((flags & TF_INNER_BATCH_TXN) == 0)
            flags |= TF_INNER_BATCH_TXN;

        source["Flags"] = flags;

        return source;
    }

    /// <summary>
    /// Нормализует внутреннюю транзакцию (object → JObject).
    /// </summary>
    public static JObject NormalizeInnerTransaction(object source)
    {
        if (source is JObject jo)
            return NormalizeInnerTransaction(jo);

        return NormalizeInnerTransaction(JObject.FromObject(source));
    }

    /// <summary>
    /// Нормализует внутреннюю транзакцию (object → JObject).
    /// </summary>
    public static async Task NormalizeBatchTransaction(
        this IXrplClient client,
        Dictionary<string, dynamic> tx)
    {
        if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
            throw new ValidationException("Batch transaction must have RawTransactions field.");

        var raws = ToRawList(rawTransactions);
        tx["RawTransactions"] = raws;

        var nextSeqByAccount = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        async Task<uint> GetNextSeqAsync(string account)
        {
            if (nextSeqByAccount.TryGetValue(account, out var seq))
                return seq;

            var probe = new Dictionary<string, dynamic> { ["Account"] = account };
            var ai = await client.AccountInfo(new AccountInfoRequest(account)
            {
                LedgerIndex = new LedgerIndex(LedgerIndexType.Current)
            });
            var start = ai.AccountData.Sequence;
            nextSeqByAccount[account] = start;
            return start;
        }

        void Bump(string account)
        {
            if (nextSeqByAccount.TryGetValue(account, out var val))
                nextSeqByAccount[account] = checked(val + 1);
        }

        var rootAccount = $"{tx["Account"]}";
        if (!tx.ContainsKey("Sequence") || tx["Sequence"] is null)
        {
            var seq = await GetNextSeqAsync(rootAccount);
            tx["Sequence"] = seq;
            nextSeqByAccount[rootAccount] = checked(seq + 1);
        }
        else
        {
            var seq = ToUInt(tx["Sequence"]);
            nextSeqByAccount[rootAccount] = checked(seq + 1);
        }

        foreach (var wrapper in raws)
        {
            if (!wrapper.TryGetValue("RawTransaction", out object? rawTxObj) || rawTxObj is null)
                throw new ValidationException("Each item in RawTransactions must contain 'RawTransaction'.");

            var normalized = NormalizeInnerTransaction(rawTxObj);
            var rawTx = normalized.ToObject<Dictionary<string, dynamic>>()!;
            wrapper["RawTransaction"] = rawTx;

            var account = rawTx.TryGetValue("Account", out object accObj)
                ? accObj?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(account))
                throw new ValidationException("Each RawTransaction must have an 'Account' field.");

            var next = await GetNextSeqAsync(account);
            rawTx["Sequence"] = next;
            Bump(account);
        }
    }

    public static List<Dictionary<string, dynamic>> ToRawList(object rawTransactions)
    {
        return rawTransactions switch
        {
            JArray ja => ja.ToObject<List<Dictionary<string, dynamic>>>()
                         ?? new List<Dictionary<string, dynamic>>(),
            IEnumerable ie when ie is not string => ie.Cast<object>()
                .Select(o => o as Dictionary<string, dynamic>
                          ?? JObject.FromObject(o!).ToObject<Dictionary<string, dynamic>>()!)
                .ToList(),
            _ => throw new ValidationException("RawTransactions must be array/collection.")
        };
    }

    /// <summary>
    /// Вычисляет transactionID для нормализованной внутренней транзакции.
    /// Алгоритм: txid = SHA512Half( HashPrefix.TXN + STObject(tx).ToBytes() ).
    /// </summary>
    public static string ComputeInnerTxId(this JObject normalizedInnerTx)
    {
        var st = Xrpl.BinaryCodec.Types.StObject.FromJson(normalizedInnerTx);
        var bytes = st.ToBytes();

        var prefix = Xrpl.BinaryCodec.Util.Bits.GetBytes((uint)Xrpl.BinaryCodec.Hashing.HashPrefix.TransactionId);
        var buf = new byte[prefix.Length + bytes.Length];
        Buffer.BlockCopy(prefix, 0, buf, 0, prefix.Length);
        Buffer.BlockCopy(bytes, 0, buf, prefix.Length, bytes.Length);

        var hash32 = Xrpl.BinaryCodec.Hashing.Sha512.Half(buf);
        return ToHex(hash32);
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

    private static uint ToUInt(object? v)
    {
        if (v is null) throw new ValidationException("Sequence is null.");
        return v switch
        {
            uint u => u,
            int i when i >= 0 => (uint)i,
            long l when l >= 0 => checked((uint)l),
            string s => uint.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            JValue jv => ToUInt(jv.Value),
            _ => Convert.ToUInt32(v, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
