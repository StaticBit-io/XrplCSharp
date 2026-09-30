using System;
using System.Collections.Generic;

namespace Xrpl.Amounts
{
    /// <summary>Why an offer left the ledger during a crossing or a payment.</summary>
    public enum OfferRemovalReason
    {
        /// <summary>It was taken in full.</summary>
        Consumed,

        /// <summary>
        /// Its owner could not fund the rest: the engine took what the owner held, or found it
        /// holding nothing of what the offer gives.
        /// </summary>
        Unfunded,

        /// <summary>Its <c>Expiration</c> had passed.</summary>
        Expired,

        /// <summary>It asked for or gave nothing.</summary>
        Empty,

        /// <summary>A trust line of what it asks for is deep-frozen.</summary>
        DeepFrozen,

        /// <summary>Its owner is no longer a member of the offer's permissioned domain.</summary>
        NotInDomain,

        /// <summary>
        /// Its owner funds so little of it that taking what they hold would improve its quality
        /// (<c>shouldRmSmallIncreasedQOffer</c>).
        /// </summary>
        TooSmall,

        /// <summary>The issuer of what it asks for requires authorization its owner does not have.</summary>
        Unauthorized,

        /// <summary>
        /// It was the account's own offer, at or better than the price of the account's new
        /// offer, which deletes it instead of crossing it.
        /// </summary>
        SelfCrossed,

        /// <summary>The <c>OfferCreate</c>'s <c>OfferSequence</c> cancelled it.</summary>
        Cancelled,
    }

    /// <summary>
    /// One offer, or one slice of an AMM pool, the engine took. The fills of a result are in the
    /// order the engine took them: pass by pass, and within a pass by the position of the book in
    /// the strand, each book's in the order the book gave them.
    /// </summary>
    public sealed class OfferFill
    {
        internal OfferFill(int pass, int strand, int step, string offerIndex, string owner, bool pool, XrplAmount @in, XrplAmount @out, XrplQuality quality)
        {
            Pass = pass;
            Strand = strand;
            Step = step;
            OfferIndex = offerIndex;
            Owner = owner;
            IsPool = pool;
            In = @in;
            Out = @out;
            Quality = quality;
        }

        /// <summary>The engine's pass that took it, from 1: each pass takes the best strand's liquidity at one quality.</summary>
        public int Pass { get; }

        /// <summary>
        /// The strand, numbered as the engine builds them: for an <c>OfferCreate</c>, 0 is the
        /// direct book and 1 the bridge through XRP; for a <c>Payment</c>, the default path first
        /// when it is used, then each distinct path of <c>Paths</c> in order.
        /// </summary>
        public int Strand { get; }

        /// <summary>The book's position in the strand, counting its account steps.</summary>
        public int Step { get; }

        /// <summary>The offer's ledger index; null for a pool.</summary>
        public string OfferIndex { get; }

        /// <summary>The offer's owner, or the pool's AMM account.</summary>
        public string Owner { get; }

        /// <summary>Whether this is a slice of an AMM pool rather than an offer.</summary>
        public bool IsPool { get; }

        /// <summary>What the offer or the pool received, before transfer fees.</summary>
        public XrplAmount In { get; }

        /// <summary>What the offer or the pool gave, before its owner's transfer fee.</summary>
        public XrplAmount Out { get; }

        /// <summary>The quality it was taken at.</summary>
        public XrplQuality Quality { get; }
    }

    /// <summary>How one strand fared in one pass of the engine.</summary>
    public enum FlowPassOutcome
    {
        /// <summary>The strand was the best of the pass, and its liquidity was taken.</summary>
        Taken,

        /// <summary>The strand gave nothing: no liquidity left, or a step failed.</summary>
        Dry,

        /// <summary>The strand's quality was worse than the limit quality.</summary>
        BelowLimitQuality,

        /// <summary>The strand's best possible quality was already worse than the limit quality, so it was not run.</summary>
        OutOfReach,
    }

    /// <summary>One strand tried in one pass of the engine.</summary>
    public sealed class FlowPass
    {
        internal FlowPass(int pass, int strand, FlowPassOutcome outcome, XrplAmount? @in, XrplAmount? @out)
        {
            Pass = pass;
            Strand = strand;
            Outcome = outcome;
            In = @in;
            Out = @out;
        }

        /// <summary>The pass, from 1.</summary>
        public int Pass { get; }

        /// <summary>The strand, numbered as in <see cref="OfferFill.Strand"/>.</summary>
        public int Strand { get; }

        /// <summary>What happened to the strand in this pass.</summary>
        public FlowPassOutcome Outcome { get; }

        /// <summary>What the strand took in; null when it was not run or gave nothing.</summary>
        public XrplAmount? In { get; }

        /// <summary>What the strand gave out; null when it was not run or gave nothing.</summary>
        public XrplAmount? Out { get; }
    }

    /// <summary>The offers a strand found it must remove, each with the reason; the first reason given stays.</summary>
    internal sealed class OfferRemovals : Dictionary<string, OfferRemovalReason>
    {
        internal OfferRemovals()
            : base(StringComparer.Ordinal)
        {
        }

        internal void Mark(string index, OfferRemovalReason reason) => TryAdd(index, reason);

        internal void Merge(OfferRemovals other)
        {
            foreach (KeyValuePair<string, OfferRemovalReason> entry in other)
                TryAdd(entry.Key, entry.Value);
        }
    }

    /// <summary>A fill as a view records it, before the pass and the strand that took it are known.</summary>
    internal sealed class FillRecord
    {
        internal int Pass { get; set; }

        internal int Strand { get; set; }

        internal int Step { get; init; }

        internal string OfferIndex { get; init; }

        internal string Owner { get; init; }

        internal bool IsPool { get; init; }

        internal XrplAmount In { get; init; }

        internal XrplAmount Out { get; init; }

        internal XrplQuality Quality { get; init; }

        internal OfferFill ToPublic() => new OfferFill(Pass, Strand, Step, OfferIndex, Owner, IsPool, In, Out, Quality);
    }
}
