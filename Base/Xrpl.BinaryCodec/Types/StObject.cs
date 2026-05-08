using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Enums;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.BinaryCodec.Util;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/types/st-object.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Class for Serializing/Deserializing objects
    /// </summary>
    public class StObject : ISerializedType
    {
        public SortedDictionary<Field, ISerializedType> Fields;
        /// <summary>
        /// Construct a STObject from a JSON object
        /// </summary>
        public StObject()
        {
            Fields = new SortedDictionary<Field, ISerializedType>();
        }

        internal class BuildFrom
        {
            public  FromParser Parser;
            public  FromJson Json;

            public BuildFrom(FromJson json, FromParser parser)
            {
                Parser = parser;
                Json = json;
            }
        }

        private static readonly Dictionary<FieldType, BuildFrom> DispatchTable = new Dictionary<FieldType, BuildFrom>
        {
            [FieldType.StObject] = new BuildFrom(FromJson, FromParser),
            [FieldType.StArray] = new BuildFrom(StArray.FromJson, StArray.FromParser),
            [FieldType.Uint8] = new BuildFrom(Uint8.FromJson, Uint8.FromParser),
            [FieldType.Uint32] = new BuildFrom(Uint32.FromJson, Uint32.FromParser),
            [FieldType.Uint64] = new BuildFrom(Uint64.FromJson, Uint64.FromParser),
            [FieldType.Uint16] = new BuildFrom(Uint16.FromJson, Uint16.FromParser),
            [FieldType.Amount] = new BuildFrom(Amount.FromJson, Amount.FromParser),
            [FieldType.Hash128] = new BuildFrom(Hash128.FromJson, Hash128.FromParser),
            [FieldType.Hash192] = new BuildFrom(Hash192.FromJson, Hash192.FromParser),
            [FieldType.Hash256] = new BuildFrom(Hash256.FromJson, Hash256.FromParser),
            [FieldType.Hash160] = new BuildFrom(Hash160.FromJson, Hash160.FromParser),
            [FieldType.AccountId] = new BuildFrom(AccountId.FromJson, AccountId.FromParser),
            [FieldType.Blob] = new BuildFrom(Blob.FromJson, Blob.FromParser),
            [FieldType.PathSet] = new BuildFrom(PathSet.FromJson, PathSet.FromParser),
            [FieldType.Vector256] = new BuildFrom(Vector256.FromJson, Vector256.FromParser),
            [FieldType.Issue] = new BuildFrom(Issue.FromJson, Issue.FromParser),
            [FieldType.Currency] = new BuildFrom(Currency.FromOracleJson, Currency.FromParser),
            [FieldType.Number] = new BuildFrom(NumberType.FromJson, NumberType.FromParser),
            [FieldType.Int32] = new BuildFrom(Int32Type.FromJson, Int32Type.FromParser),
            [FieldType.Int64] = new BuildFrom(Int64Type.FromJson, Int64Type.FromParser),
            [FieldType.XChainBridge] = new BuildFrom(XChainBridgeType.FromJson, XChainBridgeType.FromParser),
        };

        internal static void EnsureDispatch(Field field)
        {
            if (field is TransactionTypeField)
            {
                field.FromJson = TransactionType.Values.FromJson;
                field.FromParser = TransactionType.Values.FromParser;
            }
            else if (field is EngineResultField)
            {
                field.FromJson = EngineResult.Values.FromJson;
                field.FromParser = EngineResult.Values.FromParser;
            }
            else if (field is LedgerEntryTypeField)
            {
                field.FromJson = LedgerEntryType.Values.FromJson;
                field.FromParser = LedgerEntryType.Values.FromParser;
            }
            else if (DispatchTable.TryGetValue(field.Type, out BuildFrom buildFrom))
            {
                field.FromJson = buildFrom.Json;
                field.FromParser = buildFrom.Parser;
            }
        }

        /// <summary>
        /// Construct a STObject from a BinaryParser
        /// </summary>
        /// <param name="parser">BinaryParser to read STObject from</param>
        /// <param name="hint"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">has field as null</exception>
        public static StObject FromParser(BinaryParser parser, int? hint = null)
        {
            var so = new StObject();
            if (hint != null)
            {
                hint = parser.Pos() + hint;
            }
            while (!parser.End(hint))
            {
                var field = parser.ReadField();
                if (field == Field.ObjectEndMarker)
                {
                    break;
                }
                var sizeHint = field.IsVlEncoded ? parser.ReadVlLength() : (int?)null;
                var st = field.FromParser(parser, sizeHint);
                so.Fields[field] = st ?? throw new InvalidOperationException("Parsed " + field + " as null");
            }
            return so;
        }
        /// <summary>
        /// Construct a STObject from a JSON object
        /// </summary>
        /// <param name="token">An object to include</param>
        /// <returns></returns>
        public static StObject FromJson(JsonNode token)
        {
            return FromJson(token, false);
        }
        /// <summary>
        /// Construct a STObject from a JSON object
        /// </summary>
        /// <param name="token">An object to include</param>
        /// <param name="strict">optional, denote which field to include in serialized object</param>
        /// <returns></returns>
        /// <exception cref="InvalidJsonException">unknown field or token is not an object</exception>
        public static StObject FromJson(JsonNode token, bool signingOnly)
        {
            if (!(token is JsonObject))
                throw new InvalidJsonException($"{token.GetValueKind()} is not an object");

            var so = new StObject();
            foreach (KeyValuePair<string, JsonNode?> pair in token.AsObject())
            {
                if (!Field.Values.Has(pair.Key))
                {
                    if (signingOnly)
                        throw new InvalidJsonException($"unknown field {pair.Key}");
                    continue;
                }
                var fieldForType = Field.Values[pair.Key];
                var jsonForField = pair.Value;
                ISerializedType st;
                try
                {
                    st = fieldForType.FromJson(jsonForField);
                }
                catch (Exception e) when (e is InvalidOperationException || e is FormatException || e is OverflowException || e is PrecisionException)
                {
                    throw new InvalidJsonException($"Can't decode `{fieldForType}` from `{jsonForField}`", e);
                }
                so.Fields[fieldForType] = st;
            }
            return signingOnly ? so.FilterIsSigning() : so;
        }

        /// <inheritdoc />
        public void ToBytes(IBytesSink to)
        {
            ToBytes(to, null);
        }

        /// <inheritdoc />
        public JsonNode ToJson()
        {
            return ToJsonObject();
        }
        /// <summary>
        /// Get the JSON interpretation of this.bytes
        /// </summary>
        public JsonObject ToJsonObject()
        {
            JsonObject json = new JsonObject();
            foreach (KeyValuePair<Field, ISerializedType> pair in Fields)
            {
                json[(string)pair.Key] = pair.Value.ToJson();
            }
            return json;
        }
        /// <summary> to bytes Sink </summary>
        /// <param name="to"> bytes Sink container</param>
        /// <param name="p">field selector</param>
        public void ToBytes(IBytesSink to, Func<Field, bool> p)
        {
            var serializer = new BinarySerializer(to);
            foreach (var pair in Fields
                         .OrderBy(pair => pair.Key.Ordinal)
                         .Where(pair => pair.Key.IsSerialised && (p == null || p(pair.Key))))
            {
                serializer.Add(pair.Key, pair.Value);
            }
        }
        /// <summary>
        /// Construct a STObject from a JSON object
        /// </summary>
        /// <param name="token">An object to include</param>
        public static implicit operator StObject(JsonNode token) => FromJson(token);
        /// <summary>
        /// Construct a STObject from a hex string
        /// </summary>
        /// <param name="s">hex string</param>
        public static StObject FromHex(string s)
        {
            return FromParser(new BufferParser(s));
        }
        /// <summary>
        /// check that object contains field
        /// </summary>
        /// <param name="field">field</param>
        /// <returns></returns>
        public bool Has(Field field)
        {
            return Fields.ContainsKey(field);
        }
        /// <summary>
        /// Signing this data
        /// </summary>
        /// <returns></returns>
        public byte[] SigningData()
        {
            var list = new BytesList();
            list.Put(HashPrefix.TransactionSig.Bytes());
            ToBytes(list, f => f.IsSigningField);
            return list.ToBytes();
        }
        /// <summary>
        /// this object to bytes array
        /// </summary>
        /// <returns></returns>
        public byte[] ToBytes()
        {
            var list = new BytesList();
            ToBytes(list, f => f.IsSerialised);
            return list.ToBytes();
        }
        /// <summary>
        /// add <see cref="AccountIdField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="AccountIdField"/>field</param>
        /// <returns></returns>
        public AccountId this[AccountIdField f]
        {
            get { return (AccountId)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="AmountField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="AmountField"/>field</param>
        /// <returns></returns>
        public Amount this[AmountField f]
        {
            get { return (Amount)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="BlobField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="BlobField"/>field</param>
        /// <returns></returns>
        public Blob this[BlobField f]
        {
            get { return (Blob)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Hash128Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Hash128Field"/>field</param>
        /// <returns></returns>
        public Hash128 this[Hash128Field f]
        {
            get { return (Hash128)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Hash160Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Hash160Field"/>field</param>
        /// <returns></returns>
        public Hash160 this[Hash160Field f]
        {
            get { return (Hash160)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Hash192Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Hash192Field"/>field</param>
        /// <returns></returns>
        public Hash192 this[Hash192Field f]
        {
            get { return (Hash192)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Hash256Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Hash256Field"/>field</param>
        /// <returns></returns>
        public Hash256 this[Hash256Field f]
        {
            get { return (Hash256)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="PathSetField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="PathSetField"/>field</param>
        /// <returns></returns>
        public PathSet this[PathSetField f]
        {
            get { return (PathSet)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="StArrayField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="StArrayField"/>field</param>
        /// <returns></returns>
        public StArray this[StArrayField f]
        {
            get { return (StArray)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="StObjectField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="StObjectField"/>field</param>
        /// <returns></returns>
        public StObject this[StObjectField f]
        {
            get { return (StObject)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Uint16Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Uint16Field"/>field</param>
        /// <returns></returns>
        public Uint16 this[Uint16Field f]
        {
            get { return (Uint16)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="LedgerEntryTypeField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="LedgerEntryTypeField"/>field</param>
        /// <returns></returns>
        public LedgerEntryType this[LedgerEntryTypeField f]
        {
            get { return (LedgerEntryType)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="TransactionTypeField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="TransactionTypeField"/>field</param>
        /// <returns></returns>
        public TransactionType this[TransactionTypeField f]
        {
            get { return (TransactionType)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Uint32Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Uint32Field"/>field</param>
        /// <returns></returns>
        public Uint32 this[Uint32Field f]
        {
            get { return (Uint32)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Uint64Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Uint64Field"/>field</param>
        /// <returns></returns>
        public Uint64 this[Uint64Field f]
        {
            get { return (Uint64)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Uint8Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Uint8Field"/>field</param>
        /// <returns></returns>
        public Uint8 this[Uint8Field f]
        {
            get { return (Uint8)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="EngineResultField"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="EngineResultField"/>field</param>
        /// <returns></returns>
        public EngineResult this[EngineResultField f]
        {
            get { return (EngineResult)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// add <see cref="Vector256Field"/> field to this object
        /// </summary>
        /// <param name="f"><see cref="Vector256Field"/>field</param>
        /// <returns></returns>
        public Vector256 this[Vector256Field f]
        {
            get { return (Vector256)Fields[f]; }
            set { Fields[f] = value; }
        }
        /// <summary>
        /// Set flag to this object
        /// </summary>
        /// <param name="flags">flag</param>
        /// <returns></returns>
        public StObject SetFlag(uint flags)
        {
            if (Has(Field.Flags))
            {
                flags |= this[Field.Flags];
            }
            this[Field.Flags] = flags;
            return this;
        }
    }

    internal static class Extensions
    {
        internal static StObject FilterIsSigning(this StObject sto)
        {
            var filtered = new SortedDictionary<Field, ISerializedType>();
            foreach (var field in sto.Fields)
            {
                if (field.Key.IsSigningField)
                {
                    filtered.Add(field.Key, field.Value);
                }
            }
            sto.Fields = filtered;
            return sto;
        }

        internal static byte[] Bytes(this HashPrefix hp)
        {
            return Bits.GetBytes((uint)hp);
        }
    }
}