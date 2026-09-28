using System;
using System.Collections.Generic;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// The ledger state an <c>OfferCreate</c> crosses against: the offers in the books it can
    /// reach, the AMM pools on those books, and what the accounts involved hold. A crossing
    /// computed by <see cref="OfferCreateCrossing"/> is exact against this state; it is only as
    /// current as the state is.
    /// </summary>
    /// <remarks>
    /// <see cref="FromNodeAsync"/> reads everything from a node. A snapshot built by hand must list
    /// every offer of each book it covers, in book order - as <c>book_offers</c> returns them -
    /// and every account whose balance the crossing reads: the taker, each offer's owner, and
    /// each issuer with a transfer rate or tick size.
    /// </remarks>
    public sealed partial class DexSnapshot
    {
        /// <summary>
        /// The close time of the ledger before the one the transaction lands in, in seconds since
        /// the Ripple epoch. An offer whose <c>Expiration</c> is not after it is removed, not crossed.
        /// </summary>
        public uint ParentCloseTime { get; init; }

        /// <summary>The account reserve, in drops.</summary>
        public ulong ReserveBase { get; init; }

        /// <summary>The reserve per owned object, in drops.</summary>
        public ulong ReserveIncrement { get; init; }

        /// <summary>The accounts the crossing reads.</summary>
        public IReadOnlyList<DexAccount> Accounts { get; init; } = Array.Empty<DexAccount>();

        /// <summary>The trust lines the crossing reads, each from its holder's side.</summary>
        public IReadOnlyList<DexTrustLine> TrustLines { get; init; } = Array.Empty<DexTrustLine>();

        /// <summary>The offers of the books the crossing can reach, each book in book order.</summary>
        public IReadOnlyList<DexOffer> Offers { get; init; } = Array.Empty<DexOffer>();

        /// <summary>The AMM pools on those books.</summary>
        public IReadOnlyList<DexAmmPool> Pools { get; init; } = Array.Empty<DexAmmPool>();
    }

    /// <summary>An account as the crossing reads it: its XRP, its reserve and, for an issuer, its settings.</summary>
    public sealed class DexAccount
    {
        /// <summary>The account's address.</summary>
        public string Address { get; init; }

        /// <summary>The account's XRP balance, in drops, before the transaction's fee.</summary>
        public ulong Balance { get; init; }

        /// <summary>The objects the account owns (<c>OwnerCount</c>).</summary>
        public uint OwnerCount { get; init; }

        /// <summary>The issuer's <c>TransferRate</c> in billionths; 0 or 1,000,000,000 for none.</summary>
        public uint TransferRate { get; init; }

        /// <summary>The issuer's <c>TickSize</c>; 0 for none.</summary>
        public byte TickSize { get; init; }

        /// <summary>Whether the issuer has frozen every trust line it issues on (<c>lsfGlobalFreeze</c>).</summary>
        public bool GlobalFreeze { get; init; }
    }

    /// <summary>A trust line from its holder's side.</summary>
    public sealed class DexTrustLine
    {
        /// <summary>The holder.</summary>
        public string Account { get; init; }

        /// <summary>
        /// What the holder holds: the currency and its issuer, positive when the issuer owes the
        /// holder.
        /// </summary>
        public XrplAmount Balance { get; init; }

        /// <summary>Whether the issuer has frozen the line on its side.</summary>
        public bool Frozen { get; init; }

        /// <summary>Whether the line is deep-frozen by either side, which removes the holder's offers that buy the currency.</summary>
        public bool DeepFrozen { get; init; }
    }

    /// <summary>An offer in a book.</summary>
    public sealed class DexOffer
    {
        /// <summary>The offer's ledger index.</summary>
        public string Index { get; init; }

        /// <summary>The offer's owner.</summary>
        public string Account { get; init; }

        /// <summary>What the owner still asks for.</summary>
        public XrplAmount TakerPays { get; init; }

        /// <summary>What the owner still gives.</summary>
        public XrplAmount TakerGets { get; init; }

        /// <summary>The quality of the offer's book directory, which it is crossed at (<see cref="XrplQuality.FromBookDirectory"/>).</summary>
        public XrplQuality Quality { get; init; }

        /// <summary>The offer's <c>Expiration</c>, in seconds since the Ripple epoch; null for none.</summary>
        public uint? Expiration { get; init; }
    }

    /// <summary>An AMM pool as the payment engine reads it.</summary>
    public sealed class DexAmmPool
    {
        /// <summary>The AMM's account.</summary>
        public string Account { get; init; }

        /// <summary>The pool's balance of its first asset.</summary>
        public XrplAmount Balance { get; init; }

        /// <summary>The pool's balance of its second asset.</summary>
        public XrplAmount Balance2 { get; init; }

        /// <summary>Whether the first asset is frozen for the AMM, which leaves the pool out of the book.</summary>
        public bool AssetFrozen { get; init; }

        /// <summary>Whether the second asset is frozen for the AMM.</summary>
        public bool Asset2Frozen { get; init; }

        /// <summary>The pool's trading fee, in 1/100,000.</summary>
        public ushort TradingFee { get; init; }

        /// <summary>The auction slot's discounted fee, in 1/100,000.</summary>
        public ushort DiscountedFee { get; init; }

        /// <summary>The auction slot's holder; null when the slot is empty.</summary>
        public string SlotAccount { get; init; }

        /// <summary>The accounts the slot holder authorized to trade at its fee.</summary>
        public IReadOnlyList<string> SlotAuthAccounts { get; init; } = Array.Empty<string>();

        /// <summary>When the slot expires, in seconds since the Ripple epoch; null when there is no slot.</summary>
        public uint? SlotExpiration { get; init; }

        /// <summary>
        /// <c>getTradingFee</c>: the discounted fee while the slot has not expired, for its holder
        /// and the accounts it authorized; the trading fee otherwise.
        /// </summary>
        public ushort FeeFor(string account, uint parentCloseTime)
        {
            if (SlotExpiration is not { } expiration || parentCloseTime >= expiration)
                return TradingFee;

            if (SlotAccount != null && string.Equals(SlotAccount, account, StringComparison.Ordinal))
                return DiscountedFee;

            foreach (string authorized in SlotAuthAccounts)
            {
                if (string.Equals(authorized, account, StringComparison.Ordinal))
                    return DiscountedFee;
            }

            return TradingFee;
        }
    }
}
