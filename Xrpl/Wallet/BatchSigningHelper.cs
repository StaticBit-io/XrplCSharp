using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;

namespace Xrpl.Wallet;

/// <summary>
/// Helper methods for batch transaction signing operations.
/// For general signer utilities, see <see cref="SignerUtilities"/>.
/// </summary>
public static class BatchSigningHelper
{
    public static JsonArray SortBatchSigners(JsonArray batchSigners)
    {
        foreach (var wrapper in batchSigners)
        {
            if (wrapper is not JsonObject wrapperObj) continue;
            var bs = wrapperObj["BatchSigner"]?.AsObject() ?? wrapperObj;
            if (bs["Signers"] is JsonArray innerSigners && innerSigners.Count > 1)
            {
                bs["Signers"] = SignerUtilities.SortSignersArray(innerSigners);
            }
        }

        JsonArray sorted = new JsonArray(
            batchSigners.Select(b => b?.DeepClone()).OrderBy(b =>
            {
                var bObj = b as JsonObject;
                var bs = bObj?["BatchSigner"]?.AsObject() ?? bObj;
                var acc = bs?["Account"]?.GetValue<string>() ?? "";
                return SignerUtilities.GetAccountIdBytes(acc);
            }, SignerUtilities.ByteArrayComparer.Instance).ToArray()
        );

        // BatchV1_1: rippled rejects duplicate BatchSigner accounts with temBAD_SIGNER
        string? previousAccount = null;
        foreach (var node in sorted)
        {
            var bObj = node as JsonObject;
            var bs = bObj?["BatchSigner"]?.AsObject() ?? bObj;
            var acc = bs?["Account"]?.GetValue<string>();
            if (acc != null && string.Equals(acc, previousAccount, StringComparison.Ordinal))
                throw new InvalidOperationException($"Duplicate BatchSigner account '{acc}' is not allowed (temBAD_SIGNER).");
            previousAccount = acc;
        }

        return sorted;
    }

    public static JsonObject FindOrCreateBatchSigner(JsonArray batchSigners, string ownerAccount)
    {
        var normalized = SignerUtilities.NormalizeClassicAddress(ownerAccount);

        foreach (var wrapper in batchSigners)
        {
            if (wrapper is not JsonObject wrapperObj) continue;
            var bs = wrapperObj["BatchSigner"]?.AsObject() ?? wrapperObj;
            var acc = bs["Account"]?.GetValue<string>();
            if (string.Equals(SignerUtilities.NormalizeClassicAddress(acc ?? ""), normalized, StringComparison.OrdinalIgnoreCase))
                return bs;
        }

        var newBs = new JsonObject { ["Account"] = normalized };
        batchSigners.Add(new JsonObject { ["BatchSigner"] = newBs });
        return newBs;
    }

    /// <summary>
    /// Merges an incoming BatchSigner into an existing target BatchSigner.
    /// Handles both single-sig (SigningPubKey/TxnSignature) and multi-sig (Signers[]) cases.
    /// Preserves the original wrapper structure of signer entries.
    /// </summary>
    public static void MergeBatchSigner(JsonObject target, JsonObject incoming)
    {
        var targetSigners = target["Signers"] as JsonArray;
        var incomingSigners = incoming["Signers"] as JsonArray;

        if (incomingSigners != null)
        {
            if (targetSigners == null)
            {
                target.Remove("SigningPubKey");
                target.Remove("TxnSignature");
                targetSigners = new JsonArray();
                target["Signers"] = targetSigners;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in targetSigners)
            {
                if (s is not JsonObject sObj) continue;
                var so = sObj["Signer"]?.AsObject() ?? sObj;
                var key = $"{so["Account"]?.GetValue<string>()}|{so["SigningPubKey"]?.GetValue<string>()}|{so["TxnSignature"]?.GetValue<string>()}";
                seen.Add(key);
            }

            foreach (var s in incomingSigners)
            {
                if (s is not JsonObject sObj) continue;
                var so = sObj["Signer"]?.AsObject() ?? sObj;
                var key = $"{so["Account"]?.GetValue<string>()}|{so["SigningPubKey"]?.GetValue<string>()}|{so["TxnSignature"]?.GetValue<string>()}";
                if (seen.Add(key))
                    targetSigners.Add(sObj.DeepClone());
            }
            return;
        }

        if (targetSigners != null)
            return;

        var tPub = target["SigningPubKey"]?.GetValue<string>();
        var tSig = target["TxnSignature"]?.GetValue<string>();
        var iPub = incoming["SigningPubKey"]?.GetValue<string>();
        var iSig = incoming["TxnSignature"]?.GetValue<string>();

        if (string.Equals(tPub, iPub, StringComparison.Ordinal)
            && string.Equals(tSig, iSig, StringComparison.Ordinal))
        {
            return;
        }
    }

    /// <summary>
    /// Picks wallets from a dictionary that satisfy the quorum of a SignerList.
    /// Wallets are selected by descending weight until the quorum is met.
    /// </summary>
    /// <returns>A tuple of (selected wallets, total weight achieved, the quorum they were selected against).</returns>
    public static (List<XrplWallet> picked, uint totalWeight, uint quorum) PickWalletsForQuorum(
        LOSignerList signerList,
        IDictionary<string, XrplWallet> walletByAddr)
    {
        // SignerQuorum is a required field of a live SignerList ledger entry (never legitimately absent);
        // a null here means the caller passed a malformed/partial object, so fail loudly rather than let
        // the quorum check below silently never trip and over-pick wallets. The resolved quorum is
        // returned so callers compare against the exact value used here instead of re-reading
        // signerList.SignerQuorum themselves, which would silently depend on this method having
        // already validated it.
        if (signerList.SignerQuorum is not { } need)
        {
            throw new ValidationException("SignerList is missing SignerQuorum; cannot determine quorum for wallet selection.");
        }
        var candidates = signerList.SignerEntries
            .Select(se => (addr: se.SignerEntry.Account, w: se.SignerEntry.SignerWeight))
            .OrderByDescending(x => x.w)
            .ToList();

        uint sum = 0;
        var picked = new List<XrplWallet>();

        foreach (var (addr, w) in candidates)
        {
            if (walletByAddr.TryGetValue(addr, out var wlt))
            {
                picked.Add(wlt);
                sum += w;
                if (sum >= need) break;
            }
        }

        return (picked, sum, need);
    }

}