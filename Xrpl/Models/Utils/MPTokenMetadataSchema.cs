using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Xrpl.Models.Utils
{
    /// <summary>
    /// Defines the top-level asset classification for a Multi-Purpose Token (MPT)
    /// as specified by XLS-89.
    /// <see href="https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0089-multi-purpose-token-metadata-schema"/>
    /// </summary>
    public static class MPTokenAssetClass
    {
        /// <summary>
        /// Tokens representing real-world assets.
        /// </summary>
        public const string Rwa = "rwa";

        /// <summary>
        /// Tokens driven by community/internet culture.
        /// </summary>
        public const string Memes = "memes";

        /// <summary>
        /// Tokens representing assets from other blockchains.
        /// </summary>
        public const string Wrapped = "wrapped";

        /// <summary>
        /// Gaming tokens.
        /// </summary>
        public const string Gaming = "gaming";

        /// <summary>
        /// DeFi protocol tokens.
        /// </summary>
        public const string Defi = "defi";

        /// <summary>
        /// Other tokens.
        /// </summary>
        public const string Other = "other";
    }

    /// <summary>
    /// Defines the asset subclass for a Multi-Purpose Token (MPT)
    /// as specified by XLS-89. Required when asset_class is "rwa".
    /// <see href="https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0089-multi-purpose-token-metadata-schema"/>
    /// </summary>
    public static class MPTokenAssetSubclass
    {
        /// <summary>
        /// Stablecoin token subclass.
        /// </summary>
        public const string Stablecoin = "stablecoin";

        /// <summary>
        /// Commodity token subclass.
        /// </summary>
        public const string Commodity = "commodity";

        /// <summary>
        /// Real estate token subclass.
        /// </summary>
        public const string RealEstate = "real_estate";

        /// <summary>
        /// Private credit token subclass.
        /// </summary>
        public const string PrivateCredit = "private_credit";

        /// <summary>
        /// Equity token subclass.
        /// </summary>
        public const string Equity = "equity";

        /// <summary>
        /// Treasury token subclass.
        /// </summary>
        public const string Treasury = "treasury";

        /// <summary>
        /// Other token subclass.
        /// </summary>
        public const string Other = "other";
    }

    /// <summary>
    /// Defines the URI category for related resources in the MPT metadata
    /// as specified by XLS-89.
    /// <see href="https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0089-multi-purpose-token-metadata-schema"/>
    /// </summary>
    public static class MPTokenUriCategory
    {
        /// <summary>
        /// Website URI category.
        /// </summary>
        public const string Website = "website";

        /// <summary>
        /// Social media URI category.
        /// </summary>
        public const string Social = "social";

        /// <summary>
        /// Documentation URI category.
        /// </summary>
        public const string Docs = "docs";

        /// <summary>
        /// Other URI category.
        /// </summary>
        public const string Other = "other";
    }

    /// <summary>
    /// Represents a related URI entry in the MPT metadata schema (XLS-89).
    /// Each URI entry contains a link, its category, and a human-readable title.
    /// <see href="https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0089-multi-purpose-token-metadata-schema"/>
    /// </summary>
    public class MPTokenMetadataUri
    {
        /// <summary>
        /// URI to the related resource.
        /// </summary>
        public string Uri { get; set; }

        /// <summary>
        /// Category of the URI: website, social, docs, or other.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Human-readable label for the URI.
        /// </summary>
        public string Title { get; set; }
    }

    /// <summary>
    /// Represents the standardized metadata schema for Multi-Purpose Tokens (MPTs)
    /// as defined by XLS-89 (Multi-Purpose Token Metadata Schema).
    /// This schema defines a baseline set of fields that support reliable parsing
    /// and integration across block explorers, indexers, wallets, and cross-chain applications.
    /// The metadata is stored on-chain in the MPTokenMetadata field (max 1024 bytes).
    /// <para>
    /// This class does not enforce field-level validation rules (e.g. ticker charset,
    /// required asset_subclass when asset_class=rwa). The XRP Ledger server validates
    /// the raw hex blob at transaction submission time. This design matches the xrpl.js
    /// approach where MPTokenMetadata is treated as freeform.
    /// </para>
    /// <see href="https://github.com/XRPLF/XRPL-Standards/tree/master/XLS-0089-multi-purpose-token-metadata-schema"/>
    /// </summary>
    public class MPTokenMetadataSchema
    {
        /// <summary>
        /// Long-to-short key mapping for top-level metadata fields (XLS-89 section 3).
        /// </summary>
        public static readonly Dictionary<string, string> LongToShortKeys = new Dictionary<string, string>
        {
            { "ticker", "t" },
            { "name", "n" },
            { "desc", "d" },
            { "icon", "i" },
            { "asset_class", "ac" },
            { "asset_subclass", "as" },
            { "issuer_name", "in" },
            { "uris", "us" },
            { "additional_info", "ai" }
        };

        /// <summary>
        /// Short-to-long key mapping for top-level metadata fields (XLS-89 section 3).
        /// </summary>
        public static readonly Dictionary<string, string> ShortToLongKeys = new Dictionary<string, string>
        {
            { "t", "ticker" },
            { "n", "name" },
            { "d", "desc" },
            { "i", "icon" },
            { "ac", "asset_class" },
            { "as", "asset_subclass" },
            { "in", "issuer_name" },
            { "us", "uris" },
            { "ai", "additional_info" }
        };

        /// <summary>
        /// Long-to-short key mapping for URI nested objects (XLS-89 section 3).
        /// </summary>
        public static readonly Dictionary<string, string> UriLongToShortKeys = new Dictionary<string, string>
        {
            { "uri", "u" },
            { "category", "c" },
            { "title", "t" }
        };

        /// <summary>
        /// Short-to-long key mapping for URI nested objects (XLS-89 section 3).
        /// </summary>
        public static readonly Dictionary<string, string> UriShortToLongKeys = new Dictionary<string, string>
        {
            { "u", "uri" },
            { "c", "category" },
            { "t", "title" }
        };

        /// <summary>
        /// Ticker symbol for the token. Required, max 6 characters, uppercase A-Z and 0-9.
        /// </summary>
        public string Ticker { get; set; }

        /// <summary>
        /// Display name of the token.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Short description of the token.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// URI to the token icon image.
        /// </summary>
        public string Icon { get; set; }

        /// <summary>
        /// Top-level asset classification: rwa, memes, wrapped, gaming, defi, or other.
        /// </summary>
        public string AssetClass { get; set; }

        /// <summary>
        /// Asset subcategory. Required when AssetClass is "rwa".
        /// Values: stablecoin, commodity, real_estate, private_credit, equity, treasury, other.
        /// </summary>
        public string AssetSubclass { get; set; }

        /// <summary>
        /// Name of the token issuer.
        /// </summary>
        public string IssuerName { get; set; }

        /// <summary>
        /// List of related URIs (website, social, docs, other).
        /// </summary>
        public List<MPTokenMetadataUri> Uris { get; set; }

        /// <summary>
        /// Freeform key-value data for additional information about the token.
        /// </summary>
        public Dictionary<string, object> AdditionalInfo { get; set; }

        /// <summary>
        /// Serializes this metadata to a compact JSON string and returns it as an uppercase hex string.
        /// Throws <see cref="InvalidOperationException"/> if the result exceeds the 1024-byte limit (XLS-89).
        /// </summary>
        /// <returns>Uppercase hex-encoded string of the compact JSON metadata.</returns>
        public string ToHex()
        {
            var json = ToJson(true);
            var bytes = Encoding.UTF8.GetBytes(json);

            if (bytes.Length > 1024)
                throw new InvalidOperationException(
                    $"MPTokenMetadata exceeds the 1024-byte limit (XLS-89). Current size: {bytes.Length} bytes");

            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// Deserializes an MPTokenMetadataSchema from a hex-encoded string.
        /// Returns null if the input is null, empty, contains invalid hex,
        /// or does not represent valid XLS-89 JSON metadata.
        /// </summary>
        /// <param name="hex">Hex-encoded metadata string.</param>
        /// <returns>Deserialized <see cref="MPTokenMetadataSchema"/> or null.</returns>
        public static MPTokenMetadataSchema FromHex(string hex)
        {
            TryFromHex(hex, out var schema);
            return schema;
        }

        /// <summary>
        /// Attempts to deserialize an MPTokenMetadataSchema from a hex-encoded string.
        /// Returns false if the hex is invalid, not valid UTF-8, or not valid XLS-89 JSON.
        /// </summary>
        /// <param name="hex">Hex-encoded metadata string.</param>
        /// <param name="schema">The deserialized schema, or null on failure.</param>
        /// <returns>True if deserialization succeeded; false otherwise.</returns>
        public static bool TryFromHex(string hex, out MPTokenMetadataSchema schema)
        {
            schema = null;

            if (string.IsNullOrEmpty(hex))
                return false;

            try
            {
                var bytes = Convert.FromHexString(hex);
                var json = Encoding.UTF8.GetString(bytes);
                schema = FromJson(json);
                return schema != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Deserializes an MPTokenMetadataSchema from a JSON string.
        /// Supports both short keys (t, n, d, ...) and long keys (ticker, name, desc, ...).
        /// </summary>
        /// <param name="json">JSON string containing token metadata.</param>
        /// <returns>Deserialized <see cref="MPTokenMetadataSchema"/>.</returns>
        public static MPTokenMetadataSchema FromJson(string json)
        {
            var obj = JObject.Parse(json);
            var schema = new MPTokenMetadataSchema();

            schema.Ticker = GetValue(obj, "t", "ticker");
            schema.Name = GetValue(obj, "n", "name");
            schema.Description = GetValue(obj, "d", "desc");
            schema.Icon = GetValue(obj, "i", "icon");
            schema.AssetClass = GetValue(obj, "ac", "asset_class");
            schema.AssetSubclass = GetValue(obj, "as", "asset_subclass");
            schema.IssuerName = GetValue(obj, "in", "issuer_name");

            var urisToken = obj["us"] ?? obj["uris"];
            if (urisToken is JArray urisArray)
            {
                schema.Uris = new List<MPTokenMetadataUri>();
                foreach (var item in urisArray)
                {
                    if (item is JObject uriObj)
                    {
                        schema.Uris.Add(new MPTokenMetadataUri
                        {
                            Uri = GetValue(uriObj, "u", "uri"),
                            Category = GetValue(uriObj, "c", "category"),
                            Title = GetValue(uriObj, "t", "title")
                        });
                    }
                }
            }

            var aiToken = obj["ai"] ?? obj["additional_info"];
            if (aiToken is JObject aiObj)
            {
                schema.AdditionalInfo = aiObj.ToObject<Dictionary<string, object>>();
            }

            return schema;
        }

        /// <summary>
        /// Serializes this metadata to a compact JSON string.
        /// </summary>
        /// <param name="useShortKeys">If true, uses short keys (t, n, d, ...); otherwise uses long keys.</param>
        /// <returns>Compact JSON string of the token metadata.</returns>
        public string ToJson(bool useShortKeys = true)
        {
            var obj = BuildJObject(useShortKeys);
            return obj.ToString(Formatting.None);
        }

        /// <summary>
        /// Returns the byte size of the serialized compact JSON (UTF-8).
        /// Useful to check the size before submitting on-chain.
        /// </summary>
        /// <returns>Size in bytes of the compact JSON representation.</returns>
        public int GetByteSize()
        {
            var json = ToJson(true);
            return Encoding.UTF8.GetBytes(json).Length;
        }

        private JObject BuildJObject(bool useShortKeys)
        {
            var obj = new JObject();

            string tk = useShortKeys ? "t" : "ticker";
            string nk = useShortKeys ? "n" : "name";
            string dk = useShortKeys ? "d" : "desc";
            string ik = useShortKeys ? "i" : "icon";
            string ack = useShortKeys ? "ac" : "asset_class";
            string ask = useShortKeys ? "as" : "asset_subclass";
            string ink = useShortKeys ? "in" : "issuer_name";
            string usk = useShortKeys ? "us" : "uris";
            string aik = useShortKeys ? "ai" : "additional_info";

            if (!string.IsNullOrEmpty(Ticker))
                obj[tk] = Ticker;
            if (!string.IsNullOrEmpty(Name))
                obj[nk] = Name;
            if (!string.IsNullOrEmpty(Description))
                obj[dk] = Description;
            if (!string.IsNullOrEmpty(Icon))
                obj[ik] = Icon;
            if (!string.IsNullOrEmpty(AssetClass))
                obj[ack] = AssetClass;
            if (!string.IsNullOrEmpty(AssetSubclass))
                obj[ask] = AssetSubclass;
            if (!string.IsNullOrEmpty(IssuerName))
                obj[ink] = IssuerName;

            if (Uris != null && Uris.Count > 0)
            {
                string uk = useShortKeys ? "u" : "uri";
                string ck = useShortKeys ? "c" : "category";
                string ttk = useShortKeys ? "t" : "title";

                var arr = new JArray();
                foreach (var uri in Uris)
                {
                    var uriObj = new JObject();
                    if (!string.IsNullOrEmpty(uri.Uri))
                        uriObj[uk] = uri.Uri;
                    if (!string.IsNullOrEmpty(uri.Category))
                        uriObj[ck] = uri.Category;
                    if (!string.IsNullOrEmpty(uri.Title))
                        uriObj[ttk] = uri.Title;
                    arr.Add(uriObj);
                }
                obj[usk] = arr;
            }

            if (AdditionalInfo != null && AdditionalInfo.Count > 0)
            {
                obj[aik] = JObject.FromObject(AdditionalInfo);
            }

            return obj;
        }

        private static string GetValue(JObject obj, string shortKey, string longKey)
        {
            var token = obj[shortKey] ?? obj[longKey];
            return token?.ToString();
        }
    }
}
