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
                // Same rule as the single-signature branch below: one account cannot be both
                // single-signed and multi-signed, and keeping either would drop the other input.
                if (!string.IsNullOrEmpty(target["TxnSignature"]?.GetValue<string>()))
                    throw new InvalidOperationException($"BatchSigner '{target["Account"]}' is single-signed in one input and multi-signed in another.");

                target.Remove("SigningPubKey");
                target.Remove("TxnSignature");
                targetSigners = new JsonArray();
                target["Signers"] = targetSigners;
            }

            // Keyed by signer account: rippled rejects a Signers list naming one account twice
            // (temBAD_SIGNER), so two different signatures from the same signer are a conflict, not
            // two entries to keep.
            var byAccount = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in targetSigners)
            {
                if (s is not JsonObject sObj) continue;
                var so = sObj["Signer"]?.AsObject() ?? sObj;
                byAccount[so["Account"]?.GetValue<string>() ?? ""] = SignatureKey(so);
            }

            foreach (var s in incomingSigners)
            {
                if (s is not JsonObject sObj) continue;
                var so = sObj["Signer"]?.AsObject() ?? sObj;
                var account = so["Account"]?.GetValue<string>() ?? "";
                var key = SignatureKey(so);
                if (byAccount.TryGetValue(account, out var existingKey))
                {
                    if (!string.Equals(existingKey, key, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Conflicting signatures from signer '{account}' for BatchSigner '{target["Account"]}'.");
                    continue;
                }

                byAccount[account] = key;
                targetSigners.Add(sObj.DeepClone());
            }
            return;
        }

        var iPub = incoming["SigningPubKey"]?.GetValue<string>();
        var iSig = incoming["TxnSignature"]?.GetValue<string>();
        if (string.IsNullOrEmpty(iSig))
            return;

        // A BatchSigner carries either one signature or a Signers list; rippled rejects one that has
        // both, so a single signature arriving for an account already multi-signed is a conflict.
        if (targetSigners != null)
            throw new InvalidOperationException($"BatchSigner '{target["Account"]}' is multi-signed in one input and single-signed in another.");

        var tPub = target["SigningPubKey"]?.GetValue<string>();
        var tSig = target["TxnSignature"]?.GetValue<string>();

        if (string.IsNullOrEmpty(tSig))
        {
            target["SigningPubKey"] = iPub;
            target["TxnSignature"] = iSig;
            return;
        }

        // Two different signatures for one account cannot both be kept, and silently picking one
        // would hand back a Batch whose signer is not the one the other input was signed by.
        if (!string.Equals(tPub, iPub, StringComparison.Ordinal)
            || !string.Equals(tSig, iSig, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Conflicting signatures for BatchSigner '{target["Account"]}'.");
        }
    }

    private static string SignatureKey(JsonObject signer)
        => $"{signer["SigningPubKey"]?.GetValue<string>()}|{signer["TxnSignature"]?.GetValue<string>()}";

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