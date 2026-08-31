using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Enums;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Xrpl.Models.Utils;
public static class BatchUtils
{
    /// <summary>
    /// Turns a list of ordinary transactions - your own C# models - into the inner RawTransactions
    /// a Batch needs (Fee = "0", SigningPubKey = "", + tfInnerBatchTxn; no TxnSignature/Signers/LastLedgerSequence).
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

        // Validated the way xrpl.js validates
        Xrpl.Models.Transactions.Validation.Validate(JsonSerializer.Deserialize<Dictionary<string, object>>(batch.ToJson(), XrplJsonOptions.Default));
        return batch;
    }

    public static RawTransactionWrapper ToBatchTx(this ITransactionRequest tx)
    {
        // 1) A Batch inside a Batch is refused
        if (tx.TransactionType == TransactionType.Batch)
            throw new ArgumentException("Nested Batch is not allowed.");

        // 2) Drop the fields a Batch forbids, and those that are not stable inside one
        tx.TransactionSignature = null;
        tx.Signers = null;
        tx.LastLedgerSequence = null;

        // 3) Fields forced to a fixed value
        tx.Fee = new Xrpl.Models.Common.Currency() { Value = "0" };
        tx.SigningPublicKey = ""; // the empty string
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

    public static BatchSignerAccounts GetBatchSignerAccounts(this Dictionary<string, object> tx)
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
            JsonArray ja => ja.Select(n => JsonSerializer.Deserialize<Dictionary<string, object>>(
                                n.ToJsonString(), XrplJsonOptions.Default))
                         .ToList(),
            IEnumerable ie => ie.Cast<object>()
                    .Select(o => o as Dictionary<string, object>
                              ?? JsonSerializer.Deserialize<Dictionary<string, object>>(
                                  JsonSerializer.Serialize(o, XrplJsonOptions.Default), XrplJsonOptions.Default)!)
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
            var rawTx = rawTxObj as Dictionary<string, object>
                        ?? JsonSerializer.Deserialize<Dictionary<string, object>>(
                            JsonSerializer.Serialize(rawTxObj, XrplJsonOptions.Default), XrplJsonOptions.Default)!;

            wrapper["RawTransaction"] = rawTx;

            if (!rawTx.TryGetValue("Account", out object accObj) || accObj is null)
                throw new ValidationException("Each RawTransaction must contain Account.");

            var acc = accObj.ToString();

            if (string.IsNullOrWhiteSpace(acc))
                throw new ValidationException("RawTransaction.Account cannot be empty.");

            void AddRequired(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    return;
                if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
                    return;
                if (seen.Add(candidate))
                    rawAccounts.Add(candidate);
            }

            // rippled Batch::preflight requiredSigners:
            // - the initiator of each inner (the Delegate for delegated inners)
            AddRequired(rawTx.TryGetValue("Delegate", out var delegateObj) &&
                        delegateObj is string delegateAcc && !string.IsNullOrWhiteSpace(delegateAcc)
                ? delegateAcc
                : acc);

            // - the Counterparty of an inner (LoanSet borrower)
            if (rawTx.TryGetValue("Counterparty", out var counterpartyObj) && counterpartyObj is string counterparty)
                AddRequired(counterparty);

            // - the Sponsor of an inner that carries the SponsorSignature marker
            if (rawTx.TryGetValue("Sponsor", out var sponsorObj) && sponsorObj is string sponsorAcc &&
                rawTx.TryGetValue("SponsorSignature", out var sponsorMarker) && sponsorMarker is not null)
                AddRequired(sponsorAcc);
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
    /// <summary>The batch's root account: the top-level Account field.</summary>
    public string Root { get; init; }

    /// <summary>Distinct non-root accounts that must sign the batch, following rippled's Batch::preflight requiredSigners: each inner transaction's Delegate when it has one and its Account otherwise, plus any Counterparty, plus any Sponsor carrying a SponsorSignature.</summary>
    public IReadOnlyList<string> InnerRequired { get; init; } = Array.Empty<string>();

    /// <summary>Those of InnerRequired that already carry a signature in BatchSigners.</summary>
    public IReadOnlyList<string> InnerSigned { get; init; } = Array.Empty<string>();

    /// <summary>Those of InnerRequired that carry NO signature in BatchSigners yet.</summary>
    public IReadOnlyList<string> InnerMissing { get; init; } = Array.Empty<string>();

    /// <summary>Whether the root carries a single signature (TxnSignature + SigningPubKey).</summary>
    public bool RootSignedSingle { get; init; }

    /// <summary>Whether the root carries an XRPL multi-signature (Signers[]).</summary>
    public bool RootSignedMulti { get; init; }

    /// <summary>Whether every inner account has signed the batch.</summary>
    public bool AllInnerSigned => InnerMissing.Count == 0;

    /// <summary>Whether the root carries any signature at all, single or multi.</summary>
    public bool IsRootSigned => RootSignedSingle || RootSignedMulti;
}

public static class BatchSignStatusExtensions
{
    /// <summary>
    /// Builds the signing status of a batch transaction from a tx dictionary.
    /// </summary>
    public static BatchSignStatus GetBatchSignStatus(this Dictionary<string, object> tx)
    {
        if (tx is null) throw new ArgumentNullException(nameof(tx));

        if (!tx.TryGetValue("TransactionType", out var ttObj) ||
            !string.Equals($"{ttObj}", "Batch", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("GetBatchSignStatus: TransactionType must be 'Batch'.");
        }

        // Reuse the existing utility to obtain the root and the raw accounts
        var accs = tx.GetBatchSignerAccounts(); // Root + Raw (distinct)
        var root = accs.Root;
        var requiredInner = accs.Raw
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Convert to a JsonObject, which makes BatchSigners easier to read
        JsonObject outer = JsonNode.Parse(
            JsonSerializer.Serialize(tx, XrplJsonOptions.Default))?.AsObject();

        var signedInner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BatchSigners: [{ BatchSigner: { Account, SigningPubKey/TxnSignature or Signers[] } }, ...]
        JsonArray batchSignersArray = outer?["BatchSigners"] as JsonArray;
        if (batchSignersArray != null)
        {
            foreach (JsonNode wrapper in batchSignersArray)
            {
                if (wrapper is not JsonObject wrapperObj) continue;
                JsonObject bs = wrapperObj["BatchSigner"]?.AsObject() ?? wrapperObj;
                string acc = bs["Account"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(acc))
                    continue;

                // Check whether there is a real signature
                bool hasSignature = false;

                JsonArray signersArr = bs["Signers"] as JsonArray;
                if (signersArr != null && signersArr.Count > 0)
                {
                    hasSignature = true;
                }
                else
                {
                    string spk = bs["SigningPubKey"]?.GetValue<string>();
                    string sig = bs["TxnSignature"]?.GetValue<string>();
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

        // Look at the root signature: either single (TxnSignature + SigningPubKey)
        // or multi (Signers[]).
        bool rootSingle = false;
        bool rootMulti = false;

        if ((tx.TryGetValue("TxnSignature", out var tsObj) && tsObj != null) &&
            (tx.TryGetValue("SigningPubKey", out var spObj) && spObj != null && !string.IsNullOrWhiteSpace($"{spObj}")))
        {
            rootSingle = true;
        }

        if (tx.TryGetValue("Signers", out var signersObj) && signersObj != null)
        {
            JsonNode jt = signersObj is JsonNode n ? n : JsonNode.Parse(
                JsonSerializer.Serialize(signersObj, XrplJsonOptions.Default));
            if (jt is JsonArray arr && arr.Count > 0)
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
    /// Quickly check whether a batch is fully signed, over the inner accounts and optionally the root.
    /// </summary>
    public static bool IsFullySignedBatch(this Dictionary<string, object> tx, bool requireRoot = false)
    {
        var st = tx.GetBatchSignStatus();
        return st.AllInnerSigned && (!requireRoot || st.IsRootSigned);
    }

    /// <summary>
    /// Convenience helper for the status of a SignatureResult.
    /// </summary>
    public static BatchSignStatus GetBatchSignStatus(this SignatureResult signed)
    {
        if (signed == null) throw new ArgumentNullException(nameof(signed));
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
            XrplBinaryCodec.Decode(signed.TxBlob).ToString(), XrplJsonOptions.Default);
        return dict.GetBatchSignStatus();
    }

    /// <summary>
    /// Convenience helper for the status of a hex blob (tx_blob).
    /// </summary>
    public static BatchSignStatus GetBatchSignStatus(this string txBlob)
    {
        if (string.IsNullOrWhiteSpace(txBlob))
            throw new ArgumentNullException(nameof(txBlob));

        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
            XrplBinaryCodec.Decode(txBlob).ToString(), XrplJsonOptions.Default);
        return dict.GetBatchSignStatus();
    }
}
