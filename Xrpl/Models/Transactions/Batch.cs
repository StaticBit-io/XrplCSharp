using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Enums;

namespace Xrpl.Models.Transactions;

[Flags]
public enum BatchFlags : uint
{
    /// <summary>
    /// In ALLORNOTHING mode, all inner transactions must succeed for any one of them to succeed.
    /// </summary>
    tfAllOrNothing = 0x00010000,

    /// <summary>
    /// ONLYONE mode means that the first transaction to succeed is the only one to succeed.
    /// All other transactions either failed or were never tried.
    /// </summary>
    tfOnlyOne = 0x00020000,

    /// <summary>
    /// UNTILFAILURE applies all transactions until the first failure. All transactions after the first failure are not applied.
    /// </summary>
    tfUntilFailure = 0x00040000,

    /// <summary>
    /// All transactions are applied, even if one or more of the inner transactions fail.
    /// </summary>
    tfIndependent = 0x00080000,
}

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

public sealed class RawTransactionWrapper
{
    [JsonConverter(typeof(TransactionConverter))]
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

public sealed class BatchResponse : TransactionResponseCommon, IBatch
{
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
    public static async Task ValidateBatch(Dictionary<string, dynamic> tx)
    {
        if (tx == null)
            throw new ArgumentException("Batch: tx is null.");
        await Common.ValidateBaseTransaction(tx);

        if (!tx.TryGetValue("TransactionType", out var transactionTypeObj) ||
            transactionTypeObj is not string transactionType ||
            !string.Equals(transactionType, "Batch", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Batch: TransactionType must be 'Batch'.");
        }

        if (!tx.TryGetValue("RawTransactions", out var rawTxsObj) || rawTxsObj is not IEnumerable<dynamic> rawTxsEnumerable)
            throw new ArgumentException("Batch: RawTransactions is required and must be non-empty.");

        var rawTxs = rawTxsEnumerable.Cast<dynamic>().ToList();
        if (rawTxs.Count == 0)
            throw new ArgumentException("Batch: RawTransactions is required and must be non-empty.");
        if (rawTxs.Count > 8)
            throw new ArgumentException("Batch: RawTransactions length must be <= 8.");

        for (var i = 0; i < rawTxs.Count; i++)
        {
            var wrapper = rawTxs[i];
            if (wrapper is not IDictionary<string, dynamic> { } wrapperDict)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] is null.");
            }

            if (!wrapperDict.TryGetValue("RawTransaction", out var innerTxObj) ||
                innerTxObj == null)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction is null.");
            }

            if (innerTxObj is not IDictionary<string, dynamic> { } innerTx)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction is not a valid object.");
            }

            if (!innerTx.TryGetValue("TransactionType", out var innerTypeObj) ||
                innerTypeObj is not string innerType ||
                string.Equals(innerType, "Batch", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] cannot be a Batch transaction (nesting is not allowed).");
            }

            if (!innerTx.TryGetValue("Flags", out var flagsObj) ||
                !(flagsObj is long flagsValue) ||
                (flagsValue & (uint)XrplGlobalFlags.tfInnerBatchTxn) == 0)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] must contain the `tfInnerBatchTxn` flag.");
            }

            if (innerTx.TryGetValue("Fee", out var feeToken) && feeToken is not null && feeToken.ToString() != "0")
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction.Fee must be string \"0\" when present.");
            }

            if (innerTx.TryGetValue("SigningPubKey", out var spkToken) && spkToken is not null && spkToken.ToString() != "")
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction.SigningPubKey must be empty string when present.");
            }

            if (innerTx.TryGetValue("TxnSignature", out var txnSig) && txnSig != null)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction.TxnSignature is not allowed inside Batch.");
            }

            if (innerTx.TryGetValue("Signers", out var signers) && signers != null)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction.Signers is not allowed inside Batch.");
            }
        }

        if (tx.TryGetValue("BatchSigners", out var batchSignersObj) && batchSignersObj is IEnumerable<dynamic> batchSignersEnumerable)
        {
            var batchSigners = batchSignersEnumerable.Cast<dynamic>().ToList();
            for (var i = 0; i < batchSigners.Count; i++)
            {
                var wrapper = batchSigners[i];
                if (wrapper is not IDictionary<string, dynamic> { } wrapperDict)
                    throw new ArgumentException($"Batch: BatchSigners[{i}] is null.");

                if (!wrapperDict.TryGetValue("BatchSigner", out var sObj) ||
                    sObj == null)
                {
                    throw new ArgumentException($"Batch: BatchSigners[{i}].BatchSigner is null.");
                }

                if (sObj is not IDictionary<string, dynamic> { } sDict ||
                    !sDict.TryGetValue("Account", out var accountObj) ||
                    string.IsNullOrWhiteSpace($"{accountObj}"))
                {
                    throw new ArgumentException($"Batch: BatchSigners[{i}].Account is required.");
                }
                // SigningPubKey / TxnSignature / Signers — опциональны
            }
        }
    }
}