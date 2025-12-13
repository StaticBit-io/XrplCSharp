using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Utils;

namespace Xrpl.Wallet;

/// <summary>
/// Helper methods for batch transaction signing operations.
/// For general signer utilities, see <see cref="SignerUtilities"/>.
/// </summary>
public static class BatchSigningHelper
{
    public static byte[] BuildBatchPreimage(JObject batchTx, uint? networkId = null)
    {
        if (!batchTx.TryGetValue("RawTransactions", out var rawToken) || rawToken is not JArray rawArray)
            throw new ValidationException("Batch must have RawTransactions array.");

        var flags = batchTx.TryGetValue("Flags", out var fv) && fv.Type == JTokenType.Integer
            ? (uint)fv.Value<long>()
            : 0u;

        var txIds = new List<string>();
        foreach (var entry in rawArray.OfType<JObject>())
        {
            var inner = entry["RawTransaction"] as JObject;
            if (inner == null)
                throw new ValidationException("Each RawTransactions entry must have RawTransaction.");

            var normalized = BatchNormalizer.NormalizeInnerTransaction((JObject)inner.DeepClone());
            txIds.Add(BatchNormalizer.ComputeInnerTxId(normalized));
        }

        return XrplBinaryCodec.EncodeForSigningBatch(flags, txIds, networkId);
    }

    public static byte[] BuildMultisignPreimage(byte[] batchPreimage, string signerAddress)
    {
        var signerAccountId = SignerUtilities.GetAccountIdBytes(signerAddress);
        var fullPreimage = new byte[batchPreimage.Length + signerAccountId.Length];
        Buffer.BlockCopy(batchPreimage, 0, fullPreimage, 0, batchPreimage.Length);
        Buffer.BlockCopy(signerAccountId, 0, fullPreimage, batchPreimage.Length, signerAccountId.Length);
        return fullPreimage;
    }

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

    public static SignatureResult CombineBatchSigners(params string[] txBlobs)
    {
        if (txBlobs == null || txBlobs.Length == 0)
            throw new ArgumentException("No transactions to combine.");

        var objs = txBlobs.Select(Decode).ToArray();

        var canon0 = Canon(objs[0]);
        if (objs.Skip(1).Any(o => !JToken.DeepEquals(canon0, Canon(o))))
            throw new InvalidOperationException("Different tx bodies; cannot combine.");

        var allBatchSigners = new List<JObject>();
        foreach (var o in objs)
        {
            if (o["BatchSigners"] is JArray arr)
            {
                foreach (var it in arr.Children<JObject>())
                    allBatchSigners.Add((JObject)it.DeepClone());
            }
        }

        var merged = MergeBatchSigners(allBatchSigners);
        var sorted = SortBatchSigners(merged);

        var outTx = (JObject)objs[0].DeepClone();
        outTx["BatchSigners"] = sorted;
        outTx["SigningPubKey"] = "";
        outTx.Remove("TxnSignature");

        string blob = XrplBinaryCodec.Encode(outTx);
        return new SignatureResult(blob, Xrpl.Utils.Hashes.HashLedger.HashSignedTx(blob));
    }

    private static JArray MergeBatchSigners(List<JObject> all)
    {
        var byAccount = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        foreach (var wrapper in all)
        {
            var bs = wrapper["BatchSigner"] as JObject ?? wrapper;
            var acc = (string?)bs["Account"] ?? "";

            if (!byAccount.TryGetValue(acc, out var existing))
            {
                byAccount[acc] = (JObject)wrapper.DeepClone();
                continue;
            }

            var existingBs = existing["BatchSigner"] as JObject ?? existing;

            if (bs["Signers"] is JArray newSigners)
            {
                var existingSigners = existingBs["Signers"] as JArray ?? new JArray();
                var seen = new HashSet<string>(
                    existingSigners.Children<JObject>().Select(KeyOf),
                    StringComparer.Ordinal);

                foreach (var s in newSigners.Children<JObject>())
                {
                    if (seen.Add(KeyOf(s)))
                        existingSigners.Add(s.DeepClone());
                }
                existingBs["Signers"] = existingSigners;
            }
            else if (!string.IsNullOrEmpty((string?)bs["TxnSignature"]))
            {
                var existingSig = (string?)existingBs["TxnSignature"];
                var existingPk = (string?)existingBs["SigningPubKey"];
                var newSig = (string?)bs["TxnSignature"];
                var newPk = (string?)bs["SigningPubKey"];

                if (string.IsNullOrEmpty(existingSig))
                {
                    existingBs["TxnSignature"] = newSig;
                    existingBs["SigningPubKey"] = newPk;
                }
            }
        }

        var result = new JArray();
        foreach (var kv in byAccount.Values)
            result.Add(kv);

        return result;
    }

    private static string KeyOf(JObject se)
    {
        var so = se["Signer"] as JObject;
        if (so == null) return "";
        return $"{(string?)so["Account"]}|{(string?)so["SigningPubKey"]}|{(string?)so["TxnSignature"]}";
    }

    private static JObject Decode(string hex)
    {
        var dec = XrplBinaryCodec.Decode(hex);
        return dec is JObject jo ? jo : JObject.FromObject(dec!);
    }

    private static JObject Canon(JObject o)
    {
        var c = (JObject)o.DeepClone();
        c.Remove("TxnSignature");
        c.Remove("SigningPubKey");
        c.Remove("Signers");
        c.Remove("BatchSigners");
        if (c["Account"] is JValue v && v.Type == JTokenType.String)
            c["Account"] = SignerUtilities.NormalizeClassicAddress((string)v!);
        return c;
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