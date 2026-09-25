using System;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Models.Methods;

namespace Xrpl.Sugar
{
    /// <summary>
    /// The amendments that change how rippled computes lending and vault amounts: which
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
        /// Whether <c>fixCleanup3_3_0</c> is enabled, which selects the <c>Number</c> rounding the
        /// ledger computes with (<see cref="NumberMantissaScale.Large330"/>).
        /// </summary>
        public bool FixCleanup3_3_0 { get; init; } = true;

        /// <summary>
        /// Whether <c>fixCleanup3_4_0</c> is enabled: a payment made exactly at
        /// <c>NextPaymentDueDate</c> is on time. Before it, it counts as late.
        /// </summary>
        public bool FixCleanup3_4_0 { get; init; } = true;

        /// <summary>The arithmetic rippled uses for a loan under these amendments.</summary>
        internal NumberContext Context => NumberContext.ForAmendments(true, FixCleanup3_2_0, FixCleanup3_3_0);

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
                FixCleanup3_2_0 = features.GetByName("fixCleanup3_2_0")?.Value?.Enabled == true,
                FixCleanup3_3_0 = features.GetByName("fixCleanup3_3_0")?.Value?.Enabled == true,
                FixCleanup3_4_0 = features.GetByName("fixCleanup3_4_0")?.Value?.Enabled == true,
            };
        }
    }
}
