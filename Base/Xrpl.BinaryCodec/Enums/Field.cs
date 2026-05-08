using System;
using Xrpl.BinaryCodec.Types;

namespace Xrpl.BinaryCodec.Enums
{
    public partial class Field : EnumItem
    {
        #region members
        public readonly bool IsSigningField;
        public readonly bool IsSerialised;
        public readonly bool IsVlEncoded;
        public readonly int NthOfType;
        public readonly FieldType Type;
        public readonly byte[] Header;

        private FromJson _fromJson;
        private FromParser _fromParser;

        public FromJson FromJson
        {
            get
            {
                if (_fromJson == null)
                    StObject.EnsureDispatch(this);
                return _fromJson;
            }
            set => _fromJson = value;
        }

        public FromParser FromParser
        {
            get
            {
                if (_fromParser == null)
                    StObject.EnsureDispatch(this);
                return _fromParser;
            }
            set => _fromParser = value;
        }

        #endregion

        private static Enumeration<Field> _values;
        public static Enumeration<Field> Values => _values ??= new Enumeration<Field>();

        public Field(string name,
            int nthOfType,
            FieldType type,
            bool isSigningField=true,
            bool isSerialised=true) :
                base(name,
                    (type.Ordinal << 16 | nthOfType))
        {
            var valid = (nthOfType > 0) && nthOfType < 256 &&
                        type.Ordinal > 0 && type.Ordinal < 256;
            Type = type;
            IsSigningField = valid && isSigningField;
            IsSerialised = valid && isSerialised;
            NthOfType = nthOfType;
            IsVlEncoded = IsVlEncodedType();
            Header = CalculateHeader();
            Values.AddEnum(this);
        }

        public static implicit operator Field(string s)
        {
            return Values[s];
        }

        private byte[] CalculateHeader()
        {
            var nth = NthOfType;
            var type = Type.Ordinal;

            if (type < 16)
            {
                if (nth < 16) // common type, common name
                    return new [] {(byte) ((type << 4) | nth)};
                // common type, uncommon name
                return new[] {(byte) (type << 4), (byte) nth};
            }
            if (nth < 16)
                // uncommon type, common name
                return new[] {(byte) nth, (byte) type};
            // uncommon type, uncommon name
            return new byte[] {0, (byte) type, (byte) nth};
        }

        private bool IsVlEncodedType()
        {
            return Type == FieldType.Vector256 ||
                   Type == FieldType.Blob ||
                   Type == FieldType.AccountId;
        }

        // ─── Special meta-fields (not in definitions.json FIELDS array) ─────

        public static readonly Field Transaction = new Field(nameof(Transaction), 1, FieldType.Transaction, isSigningField: false);
        public static readonly Field LedgerEntry = new Field(nameof(LedgerEntry), 1, FieldType.LedgerEntry, isSigningField: false);
        public static readonly Field Validation = new Field(nameof(Validation), 1, FieldType.Validation, isSigningField: false);
        public static readonly Field Metadata = new Field(nameof(Metadata), 1, FieldType.Metadata, isSigningField: false);

        // ─── Special Uint16 subtypes with custom FromJson/FromParser ─────────

        public static readonly EngineResultField TransactionResult = new EngineResultField(nameof(TransactionResult), 3);
        public static readonly TransactionTypeField TransactionType = new TransactionTypeField(nameof(TransactionType), 2);
        public static readonly LedgerEntryTypeField LedgerEntryType = new LedgerEntryTypeField(nameof(LedgerEntryType), 1);

        // ─── Sentinels ──────────────────────────────────────────────────────

        public static readonly Field Generic = new Field(nameof(Generic), 0, FieldType.Unknown, isSigningField: false);
        public static readonly Field Invalid = new Field(nameof(Invalid), -1, FieldType.Unknown, isSigningField: false);

        // ─── Out-of-order / non-standard nth (not in definitions.json) ──────

        public static readonly Hash256Field hash = new Hash256Field(nameof(hash), 257, isSigningField: false);
        public static readonly Hash256Field index = new Hash256Field(nameof(index), 258, isSigningField: false);
        public static readonly AmountField taker_gets_funded = new AmountField(nameof(taker_gets_funded), 258, isSigningField: false);
        public static readonly AmountField taker_pays_funded = new AmountField(nameof(taker_pays_funded), 259, isSigningField: false);
        public static readonly AccountIdField Target = new AccountIdField(nameof(Target), 7);
        public static readonly StObjectField ObjectEndMarker = new StObjectField(nameof(ObjectEndMarker), 1);
        public static readonly StArrayField ArrayEndMarker = new StArrayField(nameof(ArrayEndMarker), 1);
    }
}
