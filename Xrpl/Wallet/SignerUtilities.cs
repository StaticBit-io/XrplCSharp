using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.AddressCodec;
using Xrpl.Client.Json;

namespace Xrpl.Wallet;

/// <summary>
/// Shared utilities for working with signers across multisign and batch signing operations.
/// Contains common functionality used by Signer, BatchSigningHelper, and XrplWallet.
/// </summary>
public static class SignerUtilities
{
    /// <summary>
    /// Normalizes an address (classic or X-address) to a classic address format.
    /// </summary>
    /// <param name="address">The address to normalize (classic or X-address).</param>
    /// <returns>The classic address format.</returns>
    public static string NormalizeClassicAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        if (!XrplCodec.IsValidClassicAddress(address))
        {
            var x = XrplAddressCodec.XAddressToClassicAddress(address);
            return x.ClassicAddress;
        }
        return address;
    }

    /// <summary>
    /// Converts an address (classic or X-address) to its account ID bytes for sorting.
    /// </summary>
    /// <param name="address">The address to convert.</param>
    /// <returns>The account ID bytes.</returns>
    public static byte[] GetAccountIdBytes(string address) =>
        XrplCodec.DecodeAccountID(NormalizeClassicAddress(address));

    /// <summary>
    /// Deduplicates and sorts a Signers array by account ID bytes.
    /// Each Signer is keyed by (Account, SigningPubKey, TxnSignature) to remove duplicates.
    /// Handles both wrapped ({"Signer": {...}}) and unwrapped formats, preserving the original structure.
    /// </summary>
    /// <param name="signers">The signers array to deduplicate and sort.</param>
    /// <returns>A new JsonArray with deduplicated and sorted signers.</returns>
    public static JsonArray DedupeAndSortSigners(JsonArray signers)
    {
        if (signers == null || signers.Count == 0)
            return signers ?? new JsonArray();

        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in signers)
        {
            if (item is not JsonObject itemObj) continue;
            var signer = itemObj["Signer"]?.AsObject() ?? itemObj;
            var acc = NormalizeClassicAddress(signer["Account"]?.GetValue<string>() ?? "");
            var pk = signer["SigningPubKey"]?.GetValue<string>() ?? "";
            var sig = signer["TxnSignature"]?.GetValue<string>() ?? "";
            var key = $"{acc}|{pk}|{sig}";

            if (!map.ContainsKey(key))
                map[key] = itemObj.DeepClone().AsObject();
        }

        return new JsonArray(map.Values.OrderBy(j =>
        {
            var signer = j["Signer"]?.AsObject() ?? j;
            var acc = signer["Account"]?.GetValue<string>() ?? "";
            return GetAccountIdBytes(acc);
        }, ByteArrayComparer.Instance).Select(n => (JsonNode)n).ToArray());
    }

    /// <summary>
    /// Sorts a Signers array by account ID bytes without deduplication.
    /// </summary>
    /// <param name="signers">The signers array to sort.</param>
    /// <returns>A new JsonArray with sorted signers.</returns>
    public static JsonArray SortSignersArray(JsonArray signers)
    {
        return new JsonArray(
            signers.Select(s => s?.DeepClone()).OrderBy(s =>
            {
                var acc = s?["Signer"]?["Account"]?.GetValue<string>() ?? "";
                var accBytes = GetAccountIdBytes(acc);
                return BitConverter.ToString(accBytes);
            }).ToArray()
        );
    }

    /// <summary>
    /// Converts JsonNode objects to CLR types (Dictionary, List, primitives) for proper binary encoding.
    /// The XrplBinaryCodec.Encode expects native CLR types, not JsonNode instances.
    /// </summary>
    /// <param name="node">The JsonNode to convert.</param>
    /// <returns>A CLR object (Dictionary, List, or primitive).</returns>
    public static object ConvertJsonNodeToClrType(JsonNode node)
    {
        if (node == null) return null;

        if (node is JsonObject obj)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in obj)
            {
                dict[prop.Key] = ConvertJsonNodeToClrType(prop.Value);
            }
            return dict;
        }

        if (node is JsonArray arr)
        {
            var list = new List<object>();
            foreach (var item in arr)
            {
                list.Add(ConvertJsonNodeToClrType(item));
            }
            return list;
        }

        if (node is JsonValue val)
        {
            if (val.TryGetValue<int>(out var i)) return (long)i;
            if (val.TryGetValue<long>(out var l)) return l;
            if (val.TryGetValue<double>(out var d)) return d;
            if (val.TryGetValue<bool>(out var b)) return b;
            if (val.TryGetValue<string>(out var s)) return s;
            return val.ToString();
        }

        return node.ToString();
    }

    /// <summary>
    /// Comparer for byte arrays used in sorting by account ID.
    /// </summary>
    public sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? a, byte[]? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int d = a[i].CompareTo(b[i]);
                if (d != 0) return d;
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}