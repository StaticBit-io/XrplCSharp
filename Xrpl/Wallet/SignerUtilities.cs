using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

using Xrpl.AddressCodec;

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
    /// <returns>A new JArray with deduplicated and sorted signers.</returns>
    public static JArray DedupeAndSortSigners(JArray signers)
    {
        if (signers == null || signers.Count == 0)
            return signers ?? new JArray();

        var map = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var item in signers.Children<JObject>())
        {
            var signer = item["Signer"] as JObject ?? item;
            var acc = NormalizeClassicAddress((string?)signer["Account"] ?? "");
            var pk = (string?)signer["SigningPubKey"] ?? "";
            var sig = (string?)signer["TxnSignature"] ?? "";
            var key = $"{acc}|{pk}|{sig}";

            if (!map.ContainsKey(key))
                map[key] = (JObject)item.DeepClone();
        }

        return new JArray(map.Values.OrderBy(j =>
        {
            var signer = j["Signer"] as JObject ?? j;
            var acc = (string?)signer["Account"] ?? "";
            return GetAccountIdBytes(acc);
        }, ByteArrayComparer.Instance));
    }

    /// <summary>
    /// Sorts a Signers array by account ID bytes without deduplication.
    /// </summary>
    /// <param name="signers">The signers array to sort.</param>
    /// <returns>A new JArray with sorted signers.</returns>
    public static JArray SortSignersArray(JArray signers)
    {
        return new JArray(
            signers.OrderBy(s =>
            {
                var acc = (string?)s["Signer"]?["Account"] ?? "";
                var accBytes = GetAccountIdBytes(acc);
                return BitConverter.ToString(accBytes);
            })
        );
    }

    /// <summary>
    /// Converts JToken objects to CLR types (Dictionary, List, primitives) for proper binary encoding.
    /// The XrplBinaryCodec.Encode expects native CLR types, not Newtonsoft JTokens.
    /// </summary>
    /// <param name="token">The JToken to convert.</param>
    /// <returns>A CLR object (Dictionary, List, or primitive).</returns>
    public static object ConvertJTokenToClrType(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var dict = new Dictionary<string, dynamic>();
                foreach (var prop in ((JObject)token).Properties())
                {
                    dict[prop.Name] = ConvertJTokenToClrType(prop.Value);
                }
                return dict;
            case JTokenType.Array:
                var list = new List<dynamic>();
                foreach (var item in (JArray)token)
                {
                    list.Add(ConvertJTokenToClrType(item));
                }
                return list;
            case JTokenType.Integer:
                return token.Value<long>();
            case JTokenType.Float:
                return token.Value<double>();
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Boolean:
                return token.Value<bool>();
            case JTokenType.Null:
                return null;
            default:
                return token.ToString();
        }
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
