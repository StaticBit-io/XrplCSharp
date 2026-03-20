using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.Models.Ledger;

namespace Xrpl.Wallet;

/// <summary>
/// Helper methods for batch transaction signing operations.
/// For general signer utilities, see <see cref="SignerUtilities"/>.
/// </summary>
public static class BatchSigningHelper
{
    public static JArray SortBatchSigners(JArray batchSigners)
    {
        foreach (var wrapper in batchSigners.Children<JObject>())
        {
            var bs = wrapper["BatchSigner"] as JObject ?? wrapper;
            if (bs["Signers"] is JArray innerSigners && innerSigners.Count > 1)
            {
                bs["Signers"] = SignerUtilities.SortSignersArray(innerSigners);
            }
        }

        return new JArray(
            batchSigners.OrderBy(b =>
            {
                var bs = b["BatchSigner"] as JObject ?? b as JObject;
                var acc = (string?)(bs?["Account"]) ?? "";
                return SignerUtilities.GetAccountIdBytes(acc);
            }, SignerUtilities.ByteArrayComparer.Instance)
        );
    }

    public static JObject FindOrCreateBatchSigner(JArray batchSigners, string ownerAccount)
    {
        var normalized = SignerUtilities.NormalizeClassicAddress(ownerAccount);

        foreach (var wrapper in batchSigners.Children<JObject>())
        {
            var bs = wrapper["BatchSigner"] as JObject ?? wrapper;
            var acc = (string?)bs["Account"];
            if (string.Equals(SignerUtilities.NormalizeClassicAddress(acc ?? ""), normalized, StringComparison.OrdinalIgnoreCase))
                return bs;
        }

        var newBs = new JObject { ["Account"] = normalized };
        batchSigners.Add(new JObject { ["BatchSigner"] = newBs });
        return newBs;
    }

    /// <summary>
    /// Merges an incoming BatchSigner into an existing target BatchSigner.
    /// Handles both single-sig (SigningPubKey/TxnSignature) and multi-sig (Signers[]) cases.
    /// Preserves the original wrapper structure of signer entries.
    /// </summary>
    public static void MergeBatchSigner(JObject target, JObject incoming)
    {
        var targetSigners = target["Signers"] as JArray;
        var incomingSigners = incoming["Signers"] as JArray;

        if (incomingSigners != null)
        {
            if (targetSigners == null)
            {
                target.Remove("SigningPubKey");
                target.Remove("TxnSignature");
                targetSigners = new JArray();
                target["Signers"] = targetSigners;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in targetSigners.Children<JObject>())
            {
                var so = s["Signer"] as JObject ?? s;
                var key = $"{(string?)so["Account"]}|{(string?)so["SigningPubKey"]}|{(string?)so["TxnSignature"]}";
                seen.Add(key);
            }

            foreach (var s in incomingSigners.Children<JObject>())
            {
                var so = s["Signer"] as JObject ?? s;
                var key = $"{(string?)so["Account"]}|{(string?)so["SigningPubKey"]}|{(string?)so["TxnSignature"]}";
                if (seen.Add(key))
                    targetSigners.Add((JObject)s.DeepClone());
            }
            return;
        }

        if (targetSigners != null)
            return;

        var tPub = (string?)target["SigningPubKey"];
        var tSig = (string?)target["TxnSignature"];
        var iPub = (string?)incoming["SigningPubKey"];
        var iSig = (string?)incoming["TxnSignature"];

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