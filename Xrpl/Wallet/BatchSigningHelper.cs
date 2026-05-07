using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

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

        return new JsonArray(
            batchSigners.Select(b => b?.DeepClone()).OrderBy(b =>
            {
                var bObj = b as JsonObject;
                var bs = bObj?["BatchSigner"]?.AsObject() ?? bObj;
                var acc = bs?["Account"]?.GetValue<string>() ?? "";
                return SignerUtilities.GetAccountIdBytes(acc);
            }, SignerUtilities.ByteArrayComparer.Instance).ToArray()
        );
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
    /// <returns>A tuple of (selected wallets, total weight achieved)</returns>
    public static (List<XrplWallet> picked, uint totalWeight) PickWalletsForQuorum(
        LOSignerList signerList,
        IDictionary<string, XrplWallet> walletByAddr)
    {
        var need = signerList.SignerQuorum;
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

        return (picked, sum);
    }

}