using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Enums;
using Xrpl.BinaryCodec.Types;

namespace Xrpl.BinaryCodec.ShaMapTree
{
    internal class Versioner
    {
        private int _value;
        public int IncrementAndGet()
        {
            return ++_value;
        }
    }

    public class ShaMap : ShaMapInner
    {
        private Versioner _copies;
        public ShaMap() : base(0)
        {
            // This way we can copy the first to the second,
            // copy the second, then copy the first again ;)
            _copies = new Versioner();
        }
        protected ShaMap(bool isCopy, int depth) : base(isCopy, depth, 0)
        {

        }
        protected internal override ShaMapInner MakeInnerOfSameClass(int depth)
        {
            return new ShaMap(true, depth);
        }
        public virtual ShaMap Copy()
        {
            Version = _copies.IncrementAndGet();
            var copy = (ShaMap)Copy(_copies.IncrementAndGet());
            copy._copies = _copies;
            return copy;
        }
    }

    public class AccountState : ShaMap
    {
        protected AccountState(bool isCopy, int depth) : base(isCopy, depth)
        {
        }

        public AccountState()
        {
            
        }

        public bool Add(LedgerEntry entry)
        {
            return AddItem(entry.Index(), entry);
        }

        public bool Update(LedgerEntry readLedgerEntry)
        {
            return UpdateItem(readLedgerEntry.Index(), readLedgerEntry);
        }

        protected internal override ShaMapInner MakeInnerOfSameClass(int depth)
        {
            return new AccountState(true, depth);
        }

        public new AccountState Copy()
        {
            return (AccountState) base.Copy();
        }

        public static AccountState FromJson(JsonNode jToken, bool normalise=false)
        {
            var map = new AccountState();
            foreach (JsonNode item in jToken.AsArray())
            {
                JsonObject entry = item.AsObject();
                if (normalise && LedgerEntryType.FromJson(entry["LedgerEntryType"]) == LedgerEntryType.LedgerHashes)
                    continue;
                var ledgerEntry = new LedgerEntry(entry);
                map.AddItem(ledgerEntry.Index(), ledgerEntry);
            }
            return map;
        }
    }

    public class TransactionTree : ShaMap
    {
        public bool Add(TransactionResult tx)
        {
            return AddItem(tx.Hash(), tx);
        }
    }
}