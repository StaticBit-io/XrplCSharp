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
    /// <c>FromNodeAsync</c> reads everything from a node. A snapshot built by hand must list
    /// every offer of each book it covers, in book order - as <c>book_offers</c> returns them -
    /// and every account whose balance the crossing reads: the taker, each offer's owner, and
    /// each issuer with a transfer rate or tick size. A book read only in part goes in
    /// <see cref="PartialBooks"/>. For a transaction in a permissioned domain, the domain and the
    /// credentials that decide its members go in <see cref="Domains"/> and
    /// <see cref="Credentials"/>; where a hybrid offer shares a quality with domain offers, the
    /// offers are listed in the order they were placed.
    /// </remarks>
    public sealed partial class DexSnapshot
    {
        /// <summary>
        /// The close time of the ledger before the one the transaction lands in, in seconds since
        /// the Ripple epoch. An offer whose <c>Expiration</c> is not after it is removed, not crossed.
        /// </summary>
        public uint ParentCloseTime { get; init; }

        /// <summary>
        /// The sequence of the ledger the snapshot was read at; 0 for one built by hand. A
        /// deeper read of the same books can be made at it with <see cref="DexSnapshotOptions.Ledger"/>.
        /// </summary>
        public uint LedgerSequence { get; init; }

        /// <summary>The account reserve, in drops.</summary>
        public ulong ReserveBase { get; init; }

        /// <summary>The reserve per owned object, in drops.</summary>
        public ulong ReserveIncrement { get; init; }

        /// <summary>The accounts the crossing reads.</summary>
        public IReadOnlyList<DexAccount> Accounts { get; init; } = Array.Empty<DexAccount>();

        /// <summary>
        /// The trust lines the transaction reads, each listed once from one of its two accounts,
        /// with the other account's settings in the <c>Peer</c> properties.
        /// </summary>
        public IReadOnlyList<DexTrustLine> TrustLines { get; init; } = Array.Empty<DexTrustLine>();

        /// <summary>The offers of the books the crossing can reach, each book in book order.</summary>
        public IReadOnlyList<DexOffer> Offers { get; init; } = Array.Empty<DexOffer>();

        /// <summary>The AMM pools on those books.</summary>
        public IReadOnlyList<DexAmmPool> Pools { get; init; } = Array.Empty<DexAmmPool>();

        /// <summary>The permissioned domains whose books the transaction walks, or whose membership it checks.</summary>
        public IReadOnlyList<DexDomain> Domains { get; init; } = Array.Empty<DexDomain>();

        /// <summary>
        /// The credentials the transaction reads: those a domain accepts, held by the accounts
        /// whose membership is checked, and those a payment names in <c>CredentialIDs</c>.
        /// </summary>
        public IReadOnlyList<DexCredential> Credentials { get; init; } = Array.Empty<DexCredential>();

        /// <summary>
        /// The books whose offers the snapshot holds only in part: their first offers, the rest
        /// left unread. A result that walked past the last of them says so in its
        /// <c>NeedsDeeperBooks</c>.
        /// </summary>
        public IReadOnlyList<DexBook> PartialBooks { get; init; } = Array.Empty<DexBook>();
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

        /// <summary>The account's next sequence number, which an <c>OfferSequence</c> must be below.</summary>
        public uint Sequence { get; init; }

        /// <summary>Whether the account set <c>lsfDefaultRipple</c>: its new trust lines ripple, and NoRipple is its non-default setting.</summary>
        public bool DefaultRipple { get; init; }

        /// <summary>Whether the issuer refuses trust lines it did not ask for (<c>lsfDisallowIncomingTrustline</c>).</summary>
        public bool DisallowIncomingTrustline { get; init; }

        /// <summary>The issuer's <c>TransferRate</c> in billionths; 0 or 1,000,000,000 for none.</summary>
        public uint TransferRate { get; init; }

        /// <summary>The issuer's <c>TickSize</c>; 0 for none.</summary>
        public byte TickSize { get; init; }

        /// <summary>Whether the issuer has frozen every trust line it issues on (<c>lsfGlobalFreeze</c>).</summary>
        public bool GlobalFreeze { get; init; }

        /// <summary>Whether the account's issued currencies need its authorization to be held (<c>lsfRequireAuth</c>).</summary>
        public bool RequireAuth { get; init; }

        /// <summary>Whether a payment to the account must carry a destination tag (<c>lsfRequireDestTag</c>).</summary>
        public bool RequireDestinationTag { get; init; }

        /// <summary>Whether the account accepts payments only from accounts it preauthorized (<c>lsfDepositAuth</c>).</summary>
        public bool DepositAuth { get; init; }

        /// <summary>The accounts the account preauthorized to pay it (<c>DepositPreauth</c>).</summary>
        public IReadOnlyList<string> DepositPreauthorized { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The sets of credentials the account preauthorized: a payment that presents exactly one
        /// of these sets in its <c>CredentialIDs</c> is let through.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<DexCredentialType>> DepositPreauthorizedCredentials { get; init; } =
            Array.Empty<IReadOnlyList<DexCredentialType>>();
    }

    /// <summary>
    /// A trust line, described from one of its two accounts: <see cref="Account"/> and the peer
    /// named by the currency's issuer in <see cref="Balance"/>. Each side's settings are given
    /// for that side; the <c>Peer</c> properties belong to the other account.
    /// </summary>
    public sealed class DexTrustLine
    {
        /// <summary>The account the line is described from.</summary>
        public string Account { get; init; }

        /// <summary>
        /// The balance from <see cref="Account"/>'s side: the currency, with the peer as its issuer,
        /// positive when the peer owes the account.
        /// </summary>
        public XrplAmount Balance { get; init; }

        /// <summary>How much of the peer's currency the account trusts it for; null for zero.</summary>
        public XrplAmount? Limit { get; init; }

        /// <summary>How much of the account's currency the peer trusts it for; null for zero.</summary>
        public XrplAmount? PeerLimit { get; init; }

        /// <summary>The account's <c>QualityIn</c> in billionths; 0 for none.</summary>
        public uint QualityIn { get; init; }

        /// <summary>The account's <c>QualityOut</c> in billionths; 0 for none.</summary>
        public uint QualityOut { get; init; }

        /// <summary>The peer's <c>QualityIn</c> in billionths; 0 for none.</summary>
        public uint PeerQualityIn { get; init; }

        /// <summary>The peer's <c>QualityOut</c> in billionths; 0 for none.</summary>
        public uint PeerQualityOut { get; init; }

        /// <summary>Whether the account set <c>NoRipple</c> on the line.</summary>
        public bool NoRipple { get; init; }

        /// <summary>Whether the peer set <c>NoRipple</c> on the line.</summary>
        public bool PeerNoRipple { get; init; }

        /// <summary>Whether the peer has frozen the line on its side.</summary>
        public bool Frozen { get; init; }

        /// <summary>Whether the account has frozen the line on its side.</summary>
        public bool FrozenByAccount { get; init; }

        /// <summary>Whether the line is deep-frozen by either side, which removes the holder's offers that buy the currency.</summary>
        public bool DeepFrozen { get; init; }

        /// <summary>Whether the account authorized the peer to hold its currency (its <c>Auth</c> flag).</summary>
        public bool Authorized { get; init; }

        /// <summary>Whether the peer authorized the account to hold its currency.</summary>
        public bool PeerAuthorized { get; init; }

        /// <summary>
        /// Whether the line counts toward the account's owner count (its <c>Reserve</c> flag). A
        /// line whose balance returns to zero with the account's side at its defaults stops
        /// counting, and is deleted when neither side counts it.
        /// </summary>
        public bool Reserve { get; init; }

        /// <summary>Whether the line counts toward the peer's owner count.</summary>
        public bool PeerReserve { get; init; }
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

        /// <summary>
        /// The permissioned domain the offer was placed in; null for an offer of the open book.
        /// A domain offer sits in the domain's book, and a hybrid one in the open book as well.
        /// </summary>
        public string DomainId { get; init; }

        /// <summary>Whether the domain offer is also in the open book (<c>lsfHybrid</c>).</summary>
        public bool Hybrid { get; init; }
    }

    /// <summary>A book: the asset its offers ask for and the asset they give, in a domain or the open book.</summary>
    public sealed class DexBook
    {
        /// <summary>What the book's offers ask for.</summary>
        public IssuedCurrency TakerPays { get; init; }

        /// <summary>What the book's offers give.</summary>
        public IssuedCurrency TakerGets { get; init; }

        /// <summary>The permissioned domain of the book; null for the open book.</summary>
        public string DomainId { get; init; }
    }

    /// <summary>A permissioned domain: its owner, and the credentials that make an account a member.</summary>
    public sealed class DexDomain
    {
        /// <summary>The domain's ledger index (<c>DomainID</c>).</summary>
        public string DomainId { get; init; }

        /// <summary>The domain's owner, a member without a credential.</summary>
        public string Owner { get; init; }

        /// <summary>The credentials the domain accepts; an account holding any of them, accepted and unexpired, is a member.</summary>
        public IReadOnlyList<DexCredentialType> AcceptedCredentials { get; init; } = Array.Empty<DexCredentialType>();
    }

    /// <summary>A kind of credential: who issues it and its type.</summary>
    public sealed class DexCredentialType
    {
        /// <summary>The credential's issuer.</summary>
        public string Issuer { get; init; }

        /// <summary>The credential's type, hex-encoded.</summary>
        public string CredentialType { get; init; }
    }

    /// <summary>A credential an account holds.</summary>
    public sealed class DexCredential
    {
        /// <summary>The account the credential is about.</summary>
        public string Subject { get; init; }

        /// <summary>The credential's issuer.</summary>
        public string Issuer { get; init; }

        /// <summary>The credential's type, hex-encoded.</summary>
        public string CredentialType { get; init; }

        /// <summary>Whether the subject accepted the credential (<c>lsfAccepted</c>).</summary>
        public bool Accepted { get; init; }

        /// <summary>When the credential expires, in seconds since the Ripple epoch; null for never.</summary>
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
