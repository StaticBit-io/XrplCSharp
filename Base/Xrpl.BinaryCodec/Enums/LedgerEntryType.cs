using System;
using System.Text.Json.Nodes;

namespace Xrpl.BinaryCodec.Enums
{
    public partial class LedgerEntryType : SerializedEnumItem<ushort>
    {
        public class Enumeration : SerializedEnumeration<LedgerEntryType, ushort>{}
        public static Enumeration Values = new Enumeration();
        private LedgerEntryType(string name, int ordinal) : base(name, ordinal){}
        private static LedgerEntryType Add(string reference, int ordinal)
        {
            return Values.AddEnum(new LedgerEntryType(reference, ordinal));
        }

        // ─── Deprecated / legacy types ───────────────────────────────────────

        [Obsolete("GeneratorMap is deprecated and no longer used in the protocol.")]
        public static readonly LedgerEntryType GeneratorMap = Add(nameof(GeneratorMap), 'g');

        [Obsolete("Contract is deprecated and no longer used in the protocol.")]
        public static readonly LedgerEntryType Contract = Add(nameof(Contract), 'c');

        [Obsolete("Use Amendments instead. EnabledAmendments is the legacy name.")]
        public static LedgerEntryType EnabledAmendments => Amendments;

        public static LedgerEntryType FromJson(JsonNode jToken)
        {
            return Values.FromJson(jToken);
        }
    }
}
