using System.Text.Json.Serialization;

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
    [JsonPropertyName("BatchSigner")]
    [JsonRequired]
    public BatchInnerSigner Value { get; set; } = new BatchInnerSigner();

    public sealed class BatchInnerSigner
    {
        [JsonPropertyName("Account")]
        [JsonRequired]
        public string Account { get; set; } = string.Empty;

        [JsonPropertyName("SigningPubKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SigningPubKey { get; set; } = string.Empty;

        [JsonPropertyName("TxnSignature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TxnSignature { get; set; }

        [JsonPropertyName("Signers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SignerWrapper>? Signers { get; set; }
    }
}

public sealed class RawTransactionWrapper
{
    [JsonConverter(typeof(TransactionRequestConverter))]
    [JsonPropertyName("RawTransaction")]
    [JsonRequired]
    public ITransactionRequest RawTransaction { get; set; }
}

public interface IBatch : ITransactionCommon
{
    new BatchFlags? Flags { get; set; }

    List<BatchSigner>? BatchSigners { get; set; }

    List<RawTransactionWrapper> RawTransactions { get; set; }
}

public sealed class Batch : TransactionRequest, IBatch
{
    public Batch() => TransactionType = TransactionType.Batch;

    // Допустимо — 0 или 1 режим (бит из BatchFlags) вместе с обычными глобальными флагами.
    [JsonPropertyName("Flags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public new BatchFlags? Flags
    {
        get => base.Flags.HasValue ? (BatchFlags?)base.Flags.Value : null;
        set => base.Flags = (uint?)value;
    }


    [JsonPropertyName("BatchSigners")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BatchSigner>? BatchSigners { get; set; }

    [JsonPropertyName("RawTransactions")]
    [JsonRequired]
    public List<RawTransactionWrapper> RawTransactions { get; set; } = new List<RawTransactionWrapper>();
}

public sealed class BatchResponse : TransactionResponse, IBatch
{
    // Допустимо — 0 или 1 режим (бит из BatchFlags) вместе с обычными глобальными флагами.
    [JsonPropertyName("Flags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public new BatchFlags? Flags
    {
        get => base.Flags.HasValue ? (BatchFlags?)base.Flags.Value : null;
        set => base.Flags = (uint?)value;
    }

    [JsonPropertyName("BatchSigners")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BatchSigner>? BatchSigners { get; set; }

    [JsonPropertyName("RawTransactions")]
    [JsonRequired]
    public List<RawTransactionWrapper> RawTransactions { get; set; } = new List<RawTransactionWrapper>();
}

public partial class Validation
{
    public static async Task ValidateBatch(Dictionary<string, object> tx)
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

        if (!tx.TryGetValue("RawTransactions", out var rawTxsObj) || rawTxsObj is not IEnumerable<object> rawTxsEnumerable)
            throw new ArgumentException("Batch: RawTransactions is required and must be non-empty.");

        List<object> rawTxs = rawTxsEnumerable.Cast<object>().ToList();
        if (rawTxs.Count == 0)
            throw new ArgumentException("Batch: RawTransactions is required and must be non-empty.");
        if (rawTxs.Count > 8)
            throw new ArgumentException("Batch: RawTransactions length must be <= 8.");

        for (var i = 0; i < rawTxs.Count; i++)
        {
            var wrapper = rawTxs[i];
            if (wrapper is not IDictionary<string, object> { } wrapperDict)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] is null.");
            }

            if (!wrapperDict.TryGetValue("RawTransaction", out var innerTxObj) ||
                innerTxObj == null)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}].RawTransaction is null.");
            }

            if (innerTxObj is not IDictionary<string, object> { } innerTx)
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

        if (tx.TryGetValue("BatchSigners", out var batchSignersObj) && batchSignersObj is IEnumerable<object> batchSignersEnumerable)
        {
            List<object> batchSigners = batchSignersEnumerable.Cast<object>().ToList();
            for (var i = 0; i < batchSigners.Count; i++)
            {
                var wrapper = batchSigners[i];
                if (wrapper is not IDictionary<string, object> { } wrapperDict)
                    throw new ArgumentException($"Batch: BatchSigners[{i}] is null.");

                if (!wrapperDict.TryGetValue("BatchSigner", out var sObj) ||
                    sObj == null)
                {
                    throw new ArgumentException($"Batch: BatchSigners[{i}].BatchSigner is null.");
                }

                if (sObj is not IDictionary<string, object> { } sDict ||
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