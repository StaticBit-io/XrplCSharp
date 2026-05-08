using System;
using Xrpl.BinaryCodec.Enums;

namespace Xrpl.BinaryCodec.Types
{
    public partial class TransactionType : SerializedEnumItem<ushort>
    {
        public class Enumeration : SerializedEnumeration<TransactionType, ushort> { }
        public static Enumeration Values = new Enumeration();
        private TransactionType(string reference, int ordinal) : base(reference, ordinal) { }

        private static TransactionType Add(string name, int ordinal)
        {
            return Values.AddEnum(new TransactionType(name, ordinal));
        }

        /// <summary>This transaction type installs a hook. Not in the current protocol definitions.</summary>
        [Obsolete("HookSet is not in the current protocol definitions.")]
        public static readonly TransactionType HookSet = Add(nameof(HookSet), 22);
    }
}
