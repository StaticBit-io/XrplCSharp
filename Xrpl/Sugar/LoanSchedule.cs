using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// One regular payment of a <see cref="LoanSchedule"/>. Amounts are in the loan asset's own unit,
    /// drops for XRP.
    /// </summary>
    public sealed class LoanScheduleRow
    {
        /// <summary>1 for the next payment, counting up.</summary>
        public int Number { get; init; }

        /// <summary>When the payment falls due; null when the entry carries no due date.</summary>
        public DateTime? DueDate { get; init; }

        /// <summary>Principal the payment repays.</summary>
        public decimal Principal { get; init; }

        /// <summary>Interest the payment carries to the vault, net of the management fee.</summary>
        public decimal Interest { get; init; }

        /// <summary>The broker's management fee out of the interest.</summary>
        public decimal ManagementFee { get; init; }

        /// <summary><c>LoanServiceFee</c>, charged on every payment.</summary>
        public decimal ServiceFee { get; init; }

        /// <summary>What the borrower pays: principal, interest, management fee and service fee.</summary>
        public decimal Total => Principal + Interest + ManagementFee + ServiceFee;

        /// <summary><c>PrincipalOutstanding</c> after the payment.</summary>
        public decimal PrincipalOutstandingAfter { get; init; }

        /// <summary><c>TotalValueOutstanding</c> after the payment.</summary>
        public decimal TotalValueOutstandingAfter { get; init; }

        /// <summary>Whether this is the payment that clears the loan.</summary>
        public bool IsFinal { get; init; }
    }

    /// <summary>
    /// The amendments that change how rippled splits a loan payment.
    /// </summary>
    public sealed class LoanScheduleOptions
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
        public static async Task<LoanScheduleOptions> FromNodeAsync(IXrplClient client, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            ServerFeatures features = await client
                .ServerFeatures(cancellationToken: cancellationToken)
                .Typed()
                .ConfigureAwait(false);

            return new LoanScheduleOptions
            {
                FixCleanup3_2_0 = features.GetByName("fixCleanup3_2_0")?.Value?.Enabled == true,
                FixCleanup3_3_0 = features.GetByName("fixCleanup3_3_0")?.Value?.Enabled == true,
                FixCleanup3_4_0 = features.GetByName("fixCleanup3_4_0")?.Value?.Enabled == true,
            };
        }
    }

    /// <summary>
    /// Projects the regular payments of a loan from its current <c>Loan</c> entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The projection repeats rippled's <c>computePaymentComponents</c>: each payment is the step from
    /// the loan's current rounded state to the rounded theoretical state after it, capped at the
    /// rounded <c>PeriodicPayment</c>, and the last one clears what is left. It starts from the
    /// entry's own values - <c>TotalValueOutstanding</c>, <c>PrincipalOutstanding</c>,
    /// <c>ManagementFeeOutstanding</c> - which the node computed, so for a loan still to be created
    /// take them from <see cref="PreviewSugar.PreviewLoanSet"/>.
    /// </para>
    /// <para>
    /// The arithmetic is rippled's own: <see cref="XrplNumber"/> in the <see cref="NumberContext"/>
    /// the amendments select, with <c>roundToAsset</c> as <c>STAmount</c> computes it, so each row is
    /// the split the node takes. Late, full and overpayments are not projected.
    /// </para>
    /// </remarks>
    public static class LoanSchedule
    {
        /// <summary>
        /// Projects the payments the loan still has to make.
        /// </summary>
        /// <param name="loan">The <c>Loan</c> entry, as the ledger or a <c>LoanSet</c> preview holds it.</param>
        /// <param name="asset">The loan asset: the asset of the broker's vault.</param>
        /// <param name="managementFeeRate">The broker's <c>ManagementFeeRate</c>, in 1/10th of a basis point.</param>
        /// <param name="options">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The entry lacks a field the projection needs.</exception>
        /// <exception cref="OverflowException">An amount of a row is beyond <see cref="decimal"/>.</exception>
        public static IReadOnlyList<LoanScheduleRow> Project(
            LOLoan loan,
            IssuedCurrency asset,
            ushort managementFeeRate,
            LoanScheduleOptions options = null)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            options ??= new LoanScheduleOptions();
            uint remaining = loan.PaymentRemaining ?? 0;
            if (remaining == 0)
                return Array.Empty<LoanScheduleRow>();

            LoanTerms terms = LoanTerms.Of(loan, asset, managementFeeRate, options);
            NumberContext c = terms.Context;
            decimal serviceFee = ToDecimal(loan.LoanServiceFee ?? XrplNumber.Zero);
            LoanState state = LoanState.Of(loan, c);
            DateTime? dueDate = loan.NextPaymentDueDate;
            uint interval = loan.PaymentInterval ?? 0;

            List<LoanScheduleRow> rows = new List<LoanScheduleRow>((int)Math.Min(remaining, 10_000));
            for (int number = 1; remaining > 0; number++)
            {
                LoanPaymentParts step = LendingMath.RegularPayment(state, remaining, terms);
                state = state.After(step, c);
                remaining = step.IsFinal ? 0 : remaining - 1;

                rows.Add(new LoanScheduleRow
                {
                    Number = number,
                    DueDate = dueDate,
                    Principal = ToDecimal(step.Principal),
                    Interest = ToDecimal(step.Interest(c)),
                    ManagementFee = ToDecimal(step.ManagementFee),
                    ServiceFee = serviceFee,
                    PrincipalOutstandingAfter = ToDecimal(state.Principal),
                    TotalValueOutstandingAfter = ToDecimal(state.Value),
                    IsFinal = step.IsFinal,
                });

                dueDate = dueDate?.AddSeconds(interval);
            }

            return rows;
        }

        internal static decimal ToDecimal(XrplNumber number)
        {
            if (!number.TryToDecimal(out decimal value))
                throw new OverflowException($"{number} is outside the range of System.Decimal.");

            return value;
        }
    }
}
