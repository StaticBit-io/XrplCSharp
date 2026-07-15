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
    /// <summary>
    /// Transaction types rippled forbids inside a Batch
    /// (Batch::preflight kDisabledTxTypes → temINVALID_INNER_BATCH).
    /// </summary>
    private static readonly HashSet<string> DisabledInnerTxTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VaultCreate", "VaultSet", "VaultDelete", "VaultDeposit", "VaultWithdraw", "VaultClawback",
        "LoanBrokerSet", "LoanBrokerDelete", "LoanBrokerCoverDeposit", "LoanBrokerCoverWithdraw", "LoanBrokerCoverClawback",
        "LoanSet", "LoanDelete", "LoanManage", "LoanPay",
    };

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

        // rippled Batch::preflight: reserve sponsorship is not allowed on the outer Batch
        if (tx.TryGetValue("SponsorFlags", out var outerSponsorFlagsObj) &&
            Common.TryGetUInt32(outerSponsorFlagsObj, out uint outerSponsorFlags) &&
            (outerSponsorFlags & (uint)SponsorCoverage.spfSponsorReserve) != 0)
        {
            throw new ArgumentException("Batch: spfSponsorReserve is not allowed on the outer Batch transaction.");
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

            // rippled Batch::preflight kDisabledTxTypes: every Vault and Loan
            // transaction type is rejected as an inner tx (temINVALID_INNER_BATCH)
            if (DisabledInnerTxTypes.Contains(innerType))
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] TransactionType '{innerType}' is not allowed inside Batch (rippled kDisabledTxTypes).");
            }

            if (!innerTx.TryGetValue("Flags", out var flagsObj) ||
                !Common.TryGetUInt32(flagsObj, out uint flagsValue) ||
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

            // rippled Batch::preflight: fee sponsorship is not allowed on inner txs
            if (innerTx.ContainsKey("Sponsor") &&
                innerTx.TryGetValue("SponsorFlags", out var innerSponsorFlagsObj) &&
                Common.TryGetUInt32(innerSponsorFlagsObj, out uint innerSponsorFlags) &&
                (innerSponsorFlags & (uint)SponsorCoverage.spfSponsorFee) != 0)
            {
                throw new ArgumentException($"Batch: RawTransactions[{i}] fee sponsorship (spfSponsorFee) is not allowed on inner Batch transactions.");
            }

            // Co-signature markers on inners must carry no signature material -
            // the sponsor/counterparty authorizes via BatchSigners instead
            foreach (string markerField in new[] { "SponsorSignature", "CounterpartySignature" })
            {
                if (!innerTx.TryGetValue(markerField, out var markerObj) || markerObj is not IDictionary<string, object> marker)
                    continue;
                if (marker.TryGetValue("TxnSignature", out var markerSig) && markerSig != null)
                    throw new ArgumentException($"Batch: RawTransactions[{i}].{markerField} must not contain TxnSignature inside a Batch.");
                if (marker.TryGetValue("Signers", out var markerSigners) && markerSigners != null)
                    throw new ArgumentException($"Batch: RawTransactions[{i}].{markerField} must not contain Signers inside a Batch.");
                if (marker.TryGetValue("SigningPubKey", out var markerPk) && markerPk != null && $"{markerPk}" != "")
                    throw new ArgumentException($"Batch: RawTransactions[{i}].{markerField}.SigningPubKey must be empty inside a Batch.");
            }
        }

        if (tx.TryGetValue("BatchSigners", out var batchSignersObj) && batchSignersObj is IEnumerable<object> batchSignersEnumerable)
        {
            string? outerAccount = tx.TryGetValue("Account", out var outerAccountObj) ? outerAccountObj as string : null;
            HashSet<string> seenAccounts = new HashSet<string>(StringComparer.Ordinal);

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

                // BatchV1_1: дубликаты и подпись внешним аккаунтом → temBAD_SIGNER на сервере
                string signerAccount = $"{accountObj}";
                if (!seenAccounts.Add(signerAccount))
                    throw new ArgumentException($"Batch: BatchSigners[{i}].Account '{signerAccount}' is duplicated (temBAD_SIGNER).");
                if (outerAccount != null && string.Equals(signerAccount, outerAccount, StringComparison.Ordinal))
                    throw new ArgumentException($"Batch: BatchSigners[{i}].Account must not equal the outer Batch Account (temBAD_SIGNER).");
                // SigningPubKey / TxnSignature / Signers — опциональны
            }
        }
    }
}