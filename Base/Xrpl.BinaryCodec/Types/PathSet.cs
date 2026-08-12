using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;

//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-binary-codec/src/types/path-set.ts
//https://xrpl.org/serialization.html#pathset-fields

namespace Xrpl.BinaryCodec.Types
{
    /// <summary> The object representation of a Hop, an issuer AccountID, an account AccountID, and a Currency or an MPTokenIssuanceID </summary>
    public class PathHop
    {
        #region Constant for masking types of a Hop

        /// <summary> TypeAccount const byte </summary>
        public const byte TypeAccount = 0x01;
        /// <summary> TypeCurrency const byte </summary>
        public const byte TypeCurrency = 0x10;
        /// <summary> type issuer const byte </summary>
        public const byte TypeIssuer = 0x20;
        /// <summary> MPTokenIssuanceID const byte (rippled 3.2.0+, MPTokensV2 amendment) </summary>
        public const byte TypeMpt = 0x40;
        /// <summary> Every bit a hop type byte is allowed to carry </summary>
        public const byte TypeAll = TypeAccount | TypeCurrency | TypeIssuer | TypeMpt;

        #endregion
        /// <summary> account AccountID </summary>
        public readonly AccountId Account;
        /// <summary> issuer AccountID </summary>
        public readonly AccountId Issuer;
        /// <summary> Currency </summary>
        public readonly Currency Currency;
        /// <summary> MPTokenIssuanceID, mutually exclusive with <see cref="Currency"/> </summary>
        public readonly Hash192 MptIssuanceId;
        /// <summary> Hop type </summary>
        public readonly int Type;
        /// <summary> Create a Hop </summary>
        /// <param name="account">account AccountID</param>
        /// <param name="issuer">issuer AccountID</param>
        /// <param name="currency">Currency</param>
        public PathHop(AccountId account, AccountId issuer, Currency currency)
            : this(account, issuer, currency, null)
        {
        }
        /// <summary> Create a Hop </summary>
        /// <param name="account">account AccountID</param>
        /// <param name="issuer">issuer AccountID</param>
        /// <param name="currency">Currency, mutually exclusive with mptIssuanceId</param>
        /// <param name="mptIssuanceId">MPTokenIssuanceID, mutually exclusive with currency</param>
        public PathHop(AccountId account, AccountId issuer, Currency currency, Hash192 mptIssuanceId)
        {
            if (currency != null && mptIssuanceId != null)
            {
                throw new InvalidJsonException("Path step cannot hold both currency and mpt_issuance_id.");
            }

            Account = account;
            Issuer = issuer;
            Currency = currency;
            MptIssuanceId = mptIssuanceId;
            Type = SynthesizeType();
        }
        /// <summary> Deserialize Hot </summary>
        /// <param name="json">json token</param>
        /// <returns></returns>
        public static PathHop FromJson(JsonNode json)
        {
            JsonNode mptIssuanceId = json["mpt_issuance_id"];
            if (mptIssuanceId != null
                && (!(mptIssuanceId is JsonValue mptJv) || mptJv.GetValueKind() != JsonValueKind.String))
            {
                throw new InvalidJsonException("Path step property `mpt_issuance_id` must be a JSON string.");
            }

            return new PathHop(
                json["account"],
                json["issuer"],
                json["currency"],
                mptIssuanceId == null ? null : Hash192.FromJson(mptIssuanceId));
        }
        /// <summary> check that hop has issuer AccountID </summary>
        public bool HasIssuer() => Issuer != null;
        /// <summary> check that hop has currency</summary>
        public bool HasCurrency() => Currency != null;
        /// <summary> check that hop has account AccountID </summary>
        public bool HasAccount() => Account != null;
        /// <summary> check that hop has MPTokenIssuanceID </summary>
        public bool HasMpt() => MptIssuanceId != null;
        /// <summary>
        /// generate type for current hop
        /// </summary>
        /// <returns></returns>
        public int SynthesizeType()
        {
            var type = 0;

            if (HasAccount())
            {
                type |= TypeAccount;
            }
            if (HasCurrency())
            {
                type |= TypeCurrency;
            }
            if (HasIssuer())
            {
                type |= TypeIssuer;
            }
            if (HasMpt())
            {
                type |= TypeMpt;
            }
            return type;
        }
        /// <summary> Serialize Hop  </summary>
        /// <returns></returns>
        public JsonObject ToJson()
        {
            JsonObject hop = new JsonObject { ["type"] = Type };

            if (HasAccount())
            {
                hop["account"] = JsonValue.Create(Account.ToString());
            }
            if (HasCurrency())
            {
                hop["currency"] = JsonValue.Create(Currency.ToString());
            }
            if (HasMpt())
            {
                hop["mpt_issuance_id"] = JsonValue.Create(MptIssuanceId.ToString());
            }
            if (HasIssuer())
            {
                hop["issuer"] = JsonValue.Create(Issuer.ToString());
            }
            return hop;
        }
    }
    /// <summary> Class for serializing/deserializing Paths </summary>
    public class Path : List<PathHop>
    {
        /// <summary> construct a Path </summary>
        public Path()
        {
        }
        /// <summary>
        /// construct a Path from an Enumerable of Hops
        /// </summary>
        /// <param name="enumerable">Path or array of HopObjects to construct a Path</param>
        public Path(IEnumerable<PathHop> enumerable) : base(enumerable)
        {
        }
        /// <summary> Deserialize Path </summary>
        /// <param name="json">json token</param>
        /// <returns></returns>
        public static Path FromJson(JsonNode json) => new Path(json.AsArray().Select(n => PathHop.FromJson(n)));
        /// <summary> Serialize Path  </summary>
        /// <returns></returns>
        public JsonArray ToJson()
        {
            JsonArray array = new JsonArray();
            foreach (PathHop hop in this)
            {
                array.Add(hop.ToJson());
            }
            return array;
        }
    }
    /// <summary> Deserialize and Serialize the PathSet type </summary>
    public class PathSet : List<Path>, ISerializedType
    {

        #region Constants for separating Paths in a PathSet
        /// <summary>
        /// PathSeparator const
        /// </summary>
        public const byte PathSeparatorByte = 0xFF;
        /// <summary>
        /// PathsetEnd const
        /// </summary>
        public const byte PathsetEndByte = 0x00;

        #endregion
        /// <summary> Construct a PathSet </summary>
        private PathSet()
        {
            
        }
        /// <summary>
        /// Construct a PathSet from an Array of Arrays representing paths
        /// </summary>
        /// <param name="collection">A PathSet or Array of Array of HopObjects</param>
        public PathSet(IEnumerable<Path> collection) : base(collection)
        {
        }

        /// <inheritdoc />
        public void ToBytes(IBytesSink buffer)
        {
            var n = 0;
            foreach (var path in this)
            {
                if (n++ != 0)
                {
                    buffer.Put(PathSeparatorByte);
                }
                foreach (var hop in path)
                {
                    // Field order mirrors rippled STPathSet::add(): account, MPT, currency, issuer
                    buffer.Put((byte)hop.Type);
                    if (hop.HasAccount())
                    {
                        buffer.Put(hop.Account.Buffer);
                    }
                    if (hop.HasMpt())
                    {
                        buffer.Put(hop.MptIssuanceId.Buffer);
                    }
                    if (hop.HasCurrency())
                    {
                        buffer.Put(hop.Currency.Buffer);
                    }
                    if (hop.HasIssuer())
                    {
                        buffer.Put(hop.Issuer.Buffer);
                    }
                }
            }
            buffer.Put(PathsetEndByte);
        }
        /// <summary>
        /// Get the JSON representation of this PathSet
        /// </summary>
        /// <returns>Array of Array of HopObjects, representing this PathSet</returns>
        public JsonNode ToJson()
        {
            JsonArray array = new JsonArray();
            foreach (Path path in this)
            {
                array.Add(path.ToJson());
            }
            return array;
        }
        /// <summary> Deserialize PathSet </summary>
        /// <param name="token">json token</param>
        /// <returns></returns>
        public static PathSet FromJson(JsonNode token)
        {
            return new PathSet(token.AsArray().Select(n => Path.FromJson(n)));
        }
        /// <summary>
        /// Construct a PathSet from a BinaryParser
        /// </summary>
        /// <param name="parser">A BinaryParser to read PathSet from</param>
        /// <param name="hint">unused, kept for the ISerializedType parser signature</param>
        /// <returns></returns>
        public static PathSet FromParser(BinaryParser parser, int? hint=null)
        {
            var pathSet = new PathSet();
            Path path = null;
            while (!parser.End())
            {
                byte type = parser.ReadOne();
                if (type == PathsetEndByte)
                {
                    break;
                }
                if (path == null)
                {
                    path = new Path();
                    pathSet.Add(path);
                }
                if (type == PathSeparatorByte)
                {
                    if (path.Count == 0)
                    {
                        throw new BinaryCodecException("Empty path in pathset");
                    }
                    path = null;
                    continue;
                }
                if ((type & ~PathHop.TypeAll) != 0)
                {
                    throw new BinaryCodecException("Bad path element in pathset: unknown type bits");
                }
                if ((type & PathHop.TypeCurrency) != 0 && (type & PathHop.TypeMpt) != 0)
                {
                    throw new BinaryCodecException("Bad path element in pathset: both currency and MPT");
                }

                AccountId account = null;
                AccountId issuer = null;
                Currency currency = null;
                Hash192 mptIssuanceId = null;

                if ((type & PathHop.TypeAccount) != 0)
                {
                    account = AccountId.FromParser(parser);
                }
                if ((type & PathHop.TypeMpt) != 0)
                {
                    mptIssuanceId = Hash192.FromParser(parser);
                }
                if ((type & PathHop.TypeCurrency) != 0)
                {
                    currency = Currency.FromParser(parser);
                }
                if ((type & PathHop.TypeIssuer) != 0)
                {
                    issuer = AccountId.FromParser(parser);
                }
                var hop = new PathHop(account, issuer, currency, mptIssuanceId);
                path.Add(hop);

            }
            return pathSet;
        }

    }
}
