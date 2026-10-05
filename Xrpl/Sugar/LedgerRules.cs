using System;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Models.Methods;

namespace Xrpl.Sugar
{
    /// <summary>
    /// The amendments that change how rippled computes lending, vault and AMM amounts: which
    /// <c>Number</c> rounding the ledger uses and which fixes apply. Defaults are the newest rules.
    /// </summary>
    public sealed class LedgerRules
    {
        /// <summary>
        /// Whether <c>fixCleanup3_2_0</c> is enabled: the rounded target state after a payment
        /// rounds principal up and interest down, and <c>(1 + r)^n - 1</c> is evaluated without
        /// cancellation for tiny rates. Before it, both round to nearest.
        /// </summary>
        public bool FixCleanup3_2_0 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixCleanup3_1_3</c> is enabled: the amount of a loan payment is rounded
        /// toward zero to the loan's scale before the overpayment is taken from it.
        /// </summary>
        public bool FixCleanup3_1_3 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixCleanup3_3_0</c> is enabled, which selects the <c>Number</c> rounding the
        /// ledger computes with (<see cref="NumberMantissaScale.Large330"/>).
        /// </summary>
        public bool FixCleanup3_3_0 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixCleanup3_4_0</c> is enabled: a payment made exactly at
        /// <c>NextPaymentDueDate</c> is on time. Before it, it counts as late.
        /// </summary>
        public bool FixCleanup3_4_0 { get; init; } = true;

        /// <summary>
        /// Whether SingleAssetVault, LendingProtocol or MPTokensV2 is enabled, which switches
        /// rippled's <c>Number</c> to the large mantissa. Lending and vaults imply it; AMM arithmetic
        /// follows it. <see cref="FromNodeAsync"/> derives it from those amendments; set by hand, it
        /// selects the scale on its own, as rippled's tests do through <c>setMantissaScale</c>.
        /// </summary>
        public bool LargeNumbers { get; init; } = true;

        /// <summary>
        /// Whether <c>fixAMMv1_1</c> is enabled: a withdrawal by the only liquidity provider first
        /// aligns the pool's LP token balance with the provider's.
        /// </summary>
        public bool FixAMMv1_1 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixAMMv1_2</c> is enabled: when a pool cannot be brought to the order book's
        /// quality, its largest offer is still taken if it beats the book.
        /// </summary>
        public bool FixAMMv1_2 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixAMMv1_3</c> is enabled: AMM deposits and withdrawals round against the
        /// trader. <see cref="AmmLiquidity"/> follows these rules only.
        /// </summary>
        public bool FixAMMv1_3 { get; init; } = true;

        /// <summary>
        /// Whether <c>MPTokensV2</c> is enabled, which refuses a withdrawal that empties a pool
        /// side without its LP tokens or the other side. On a node it also selects the large
        /// <c>Number</c> mantissa, which <see cref="FromNodeAsync"/> records in <see cref="LargeNumbers"/>.
        /// </summary>
        public bool MPTokensV2 { get; init; }

        /// <summary>
        /// Whether <c>fixReducedOffersV2</c> is enabled: an offer cut down to an input limit is
        /// sized with <c>ceilInStrict</c>, rounded down, instead of <c>ceilIn</c>.
        /// </summary>
        public bool FixReducedOffersV2 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixFillOrKill</c> is enabled: a fill-or-kill offer without <c>tfSell</c> must
        /// receive all of its <c>TakerPays</c>, not spend all of its <c>TakerGets</c>.
        /// </summary>
        public bool FixFillOrKill { get; init; } = true;

        /// <summary>
        /// Whether <c>LendingProtocolV1_1</c> is enabled: a closed-ended vault takes deposits only
        /// in its subscription phase and pays out nothing in its investment phase.
        /// </summary>
        public bool LendingProtocolV1_1 { get; init; } = true;

        /// <summary>The arithmetic rippled uses under these amendments.</summary>
        internal NumberContext Context =>
            (OutsideTransaction
                ? NumberContext.ForAmendments(true, true, true)
                : NumberContext.ForAmendments(LargeNumbers,FixCleanup3_2_0, FixCleanup3_3_0)).WithRounding(AmbientRounding);

        /// <summary>
        /// Whether the code runs outside a transaction, as <c>ripple_path_find</c> does: rippled
        /// then has no current transaction rules, so every check made through them sees its
        /// amendment disabled, and <c>Number</c> keeps its default, large scale.
        /// </summary>
        internal bool OutsideTransaction { get; private set; }

        /// <summary>
        /// Whether the current transaction rules are set. <c>Transactor::operator()</c> sets them
        /// for every <c>doApply</c>, whatever <c>useRulesGuards</c> decides for the other steps, so
        /// only code outside a transaction sees them unset.
        /// </summary>
        private bool CurrentRulesSet => !OutsideTransaction;

        /// <summary><c>fixAMMv1_1</c> as <c>swapAssetIn</c> and <c>swapAssetOut</c> read it: from the current transaction rules.</summary>
        internal bool CurrentFixAMMv1_1 => CurrentRulesSet && FixAMMv1_1;

        /// <summary><c>fixAMMv1_3</c> as an offer's <c>checkInvariant</c> reads it: from the current transaction rules.</summary>
        internal bool CurrentFixAMMv1_3 => CurrentRulesSet && FixAMMv1_3;

        /// <summary><c>fixReducedOffersV2</c> as an offer's <c>limitIn</c> reads it: from the current transaction rules.</summary>
        internal bool CurrentFixReducedOffersV2 => CurrentRulesSet && FixReducedOffersV2;

        /// <summary>These rules as code outside a transaction sees them.</summary>
        internal LedgerRules OutsideOfTransaction()
        {
            LedgerRules copy = (LedgerRules)MemberwiseClone();
            copy.OutsideTransaction = true;
            return copy;
        }

        /// <summary>
        /// The <c>Number</c> rounding mode in effect outside any guard. A node processes a
        /// transaction to nearest; the tests replay rippled's vectors in every mode.
        /// </summary>
        internal NumberRounding AmbientRounding { get; set; } = NumberRounding.ToNearest;

        /// <summary>These rules with <paramref name="rounding"/> as the ambient mode, as a <c>Number::setround</c> scope sees them.</summary>
        internal LedgerRules WithRounding(NumberRounding rounding)
        {
            LedgerRules copy = (LedgerRules)MemberwiseClone();
            copy.AmbientRounding = rounding;
            return copy;
        }

        /// <summary>Reads the amendments from the node.</summary>
        /// <remarks>
        /// The rules come from the <c>feature</c> command, which reports the amendments the
        /// ledger's <c>Amendments</c> object records. A node can also apply amendments listed in
        /// the <c>[features]</c> stanza of its config: they act as presets of its rules without
        /// being enabled on the ledger, and <c>feature</c> reports them as disabled. A standalone
        /// test node started that way runs amendments this method sees as off, and the engine
        /// then differs from it; build the <see cref="LedgerRules"/> by hand for such a node, or
        /// enable the amendments at genesis through the <c>[amendments]</c> stanza. Mainnet,
        /// testnet and devnet enable amendments on the ledger.
        /// </remarks>
        public static async Task<LedgerRules> FromNodeAsync(IXrplClient client, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            ServerFeatures features = await client
                .ServerFeatures(cancellationToken: cancellationToken)
                .Typed()
                .ConfigureAwait(false);

            return FromFeatures(features);
        }

        /// <summary>The rules for the amendments a <c>feature</c> response reports enabled.</summary>
        internal static LedgerRules FromFeatures(ServerFeatures features)
        {
            bool Enabled(string name) => features.GetByName(name)?.Value?.Enabled == true;

            return new LedgerRules
            {
                FixCleanup3_1_3 = Enabled("fixCleanup3_1_3"),
                FixCleanup3_2_0 = Enabled("fixCleanup3_2_0"),
                FixCleanup3_3_0 = Enabled("fixCleanup3_3_0"),
                FixCleanup3_4_0 = Enabled("fixCleanup3_4_0"),
                LargeNumbers = Enabled("SingleAssetVault") || Enabled("LendingProtocol") || Enabled("MPTokensV2"),
                FixAMMv1_1 = Enabled("fixAMMv1_1"),
                FixAMMv1_2 = Enabled("fixAMMv1_2"),
                FixAMMv1_3 = Enabled("fixAMMv1_3"),
                MPTokensV2 = Enabled("MPTokensV2"),
                FixReducedOffersV2 = Enabled("fixReducedOffersV2"),
                FixFillOrKill = Enabled("fixFillOrKill"),
                LendingProtocolV1_1 = Enabled("LendingProtocolV1_1"),
            };
        }
    }
}
