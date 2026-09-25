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
        /// Whether SingleAssetVault or LendingProtocol is enabled, which switches rippled's
        /// <c>Number</c> to the large mantissa. Lending and vaults imply it; AMM arithmetic follows it.
        /// </summary>
        public bool LargeNumbers { get; init; } = true;

        /// <summary>
        /// Whether <c>fixAMMv1_1</c> is enabled: a withdrawal by the only liquidity provider first
        /// aligns the pool's LP token balance with the provider's.
        /// </summary>
        public bool FixAMMv1_1 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixAMMv1_3</c> is enabled: AMM deposits and withdrawals round against the
        /// trader. <see cref="AmmLiquidity"/> follows these rules only.
        /// </summary>
        public bool FixAMMv1_3 { get; init; } = true;

        /// <summary>
        /// Whether <c>MPTokensV2</c> is enabled, which refuses a withdrawal that empties a pool
        /// side without its LP tokens or the other side.
        /// </summary>
        public bool MPTokensV2 { get; init; }

        /// <summary>The arithmetic rippled uses under these amendments.</summary>
        internal NumberContext Context => NumberContext.ForAmendments(LargeNumbers, FixCleanup3_2_0, FixCleanup3_3_0);

        /// <summary>Reads the amendments from the node.</summary>
        public static async Task<LedgerRules> FromNodeAsync(IXrplClient client, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            ServerFeatures features = await client
                .ServerFeatures(cancellationToken: cancellationToken)
                .Typed()
                .ConfigureAwait(false);

            return new LedgerRules
            {
                FixCleanup3_1_3 = features.GetByName("fixCleanup3_1_3")?.Value?.Enabled == true,
                FixCleanup3_2_0 = features.GetByName("fixCleanup3_2_0")?.Value?.Enabled == true,
                FixCleanup3_3_0 = features.GetByName("fixCleanup3_3_0")?.Value?.Enabled == true,
                FixCleanup3_4_0 = features.GetByName("fixCleanup3_4_0")?.Value?.Enabled == true,
                LargeNumbers = features.GetByName("SingleAssetVault")?.Value?.Enabled == true
                    || features.GetByName("LendingProtocol")?.Value?.Enabled == true,
                FixAMMv1_1 = features.GetByName("fixAMMv1_1")?.Value?.Enabled == true,
                FixAMMv1_3 = features.GetByName("fixAMMv1_3")?.Value?.Enabled == true,
                MPTokensV2 = features.GetByName("MPTokensV2")?.Value?.Enabled == true,
            };
        }
    }
}
