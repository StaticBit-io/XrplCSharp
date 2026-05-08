using System;
using System.Text.Json.Nodes;

namespace Xrpl.BinaryCodec.Enums
{
    public class LedgerEntryType : SerializedEnumItem<ushort>
    {
        public class Enumeration : SerializedEnumeration<LedgerEntryType, ushort>{}
        public static Enumeration Values = new Enumeration();
        private LedgerEntryType(string name, int ordinal) : base(name, ordinal){}
        private static LedgerEntryType Add(string reference, int ordinal)
        {
            return Values.AddEnum(new LedgerEntryType(reference, ordinal));
        }

        public static readonly LedgerEntryType Invalid = Add(nameof(Invalid), -1);

        // ─── Active ledger entry types (from definitions.json) ───────────────

        public static readonly LedgerEntryType NFTokenOffer = Add(nameof(NFTokenOffer), 55);
        public static readonly LedgerEntryType Check = Add(nameof(Check), 67);
        public static readonly LedgerEntryType DID = Add(nameof(DID), 73);
        public static readonly LedgerEntryType NegativeUNL = Add(nameof(NegativeUNL), 78);
        public static readonly LedgerEntryType NFTokenPage = Add(nameof(NFTokenPage), 80);
        public static readonly LedgerEntryType SignerList = Add(nameof(SignerList), 'S');
        public static readonly LedgerEntryType Ticket = Add(nameof(Ticket), 'T');
        public static readonly LedgerEntryType AccountRoot = Add(nameof(AccountRoot), 'a');
        public static readonly LedgerEntryType DirectoryNode = Add(nameof(DirectoryNode), 'd');
        public static readonly LedgerEntryType Amendments = Add(nameof(Amendments), 'f');
        public static readonly LedgerEntryType LedgerHashes = Add(nameof(LedgerHashes), 'h');
        public static readonly LedgerEntryType Bridge = Add(nameof(Bridge), 105);
        public static readonly LedgerEntryType Offer = Add(nameof(Offer), 'o');
        public static readonly LedgerEntryType DepositPreauth = Add(nameof(DepositPreauth), 112);
        public static readonly LedgerEntryType XChainOwnedClaimID = Add(nameof(XChainOwnedClaimID), 113);
        public static readonly LedgerEntryType RippleState = Add(nameof(RippleState), 'r');
        public static readonly LedgerEntryType FeeSettings = Add(nameof(FeeSettings), 's');
        public static readonly LedgerEntryType XChainOwnedCreateAccountClaimID = Add(nameof(XChainOwnedCreateAccountClaimID), 116);
        public static readonly LedgerEntryType Escrow = Add(nameof(Escrow), 117);
        public static readonly LedgerEntryType PayChannel = Add(nameof(PayChannel), 120);
        public static readonly LedgerEntryType AMM = Add(nameof(AMM), 121);
        public static readonly LedgerEntryType MPTokenIssuance = Add(nameof(MPTokenIssuance), 126);
        public static readonly LedgerEntryType MPToken = Add(nameof(MPToken), 127);
        public static readonly LedgerEntryType Oracle = Add(nameof(Oracle), 128);
        public static readonly LedgerEntryType Credential = Add(nameof(Credential), 129);
        public static readonly LedgerEntryType PermissionedDomain = Add(nameof(PermissionedDomain), 130);
        public static readonly LedgerEntryType Delegate = Add(nameof(Delegate), 131);
        public static readonly LedgerEntryType Vault = Add(nameof(Vault), 132);
        public static readonly LedgerEntryType LoanBroker = Add(nameof(LoanBroker), 136);
        public static readonly LedgerEntryType Loan = Add(nameof(Loan), 137);

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
