using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What a <c>Loan</c> entry says about the next payment, without asking the node.
    /// </summary>
    /// <remarks>
    /// These read the entry; they do not repeat the protocol's amortization, so they bound a
    /// payment rather than compute it. The exact split of a payment - regular, late, full or
    /// overpayment - is what <see cref="PreviewSugar.PreviewLoanPay"/> reports.
    /// </remarks>
    public static class LoanPayments
    {
        /// <summary>
        /// The most the next regular payment can take, in the asset's own unit (drops for XRP): an
        /// amount that always settles it. On the final payment this is exact -
        /// <c>TotalValueOutstanding</c> plus <c>LoanServiceFee</c>. Before that it is
        /// <c>PeriodicPayment</c> rounded up to the asset (whole units for XRP and MPT, the loan's
        /// <c>LoanScale</c> for an issued currency) plus <c>LoanServiceFee</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// rippled charges a regular payment as the step from the loan's current state to the
        /// rounded theoretical state after it, capped at the rounded <c>PeriodicPayment</c>
        /// (<c>computePaymentComponents</c>). The step can come out a rounding unit lower - a 90 MPT
        /// loan over three payments with <c>PeriodicPayment</c> 30.00002 is charged 30, not 31 - and
        /// the node takes only what it charges, not the whole <c>Amount</c>. Paying this cap is
        /// therefore safe; the exact figure is what <see cref="PreviewSugar.PreviewLoanPay"/> reports.
        /// </para>
        /// <para>
        /// A late payment adds late interest and <c>LatePaymentFee</c> on top, which this does not
        /// cover; preview it.
        /// </para>
        /// </remarks>
        /// <returns>null when the entry carries no periodic payment, or a value beyond <see cref="decimal"/>.</returns>
        public static decimal? RegularPaymentCap(LOLoan loan, IssuedCurrency asset)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            decimal serviceFee = 0m;
            if (loan.LoanServiceFee is { } fee && !fee.TryToDecimal(out serviceFee))
                return null;

            if (loan.PaymentRemaining == 1)
            {
                return loan.TotalValueOutstanding is { } total && total.TryToDecimal(out decimal remaining)
                    ? remaining + serviceFee
                    : null;
            }

            if (loan.PeriodicPayment is not { } periodic || !periodic.TryToDecimal(out decimal periodicPayment))
                return null;

            return RoundUpToAsset(periodicPayment, IsIntegral(asset), loan.LoanScale ?? 0) + serviceFee;
        }

        /// <summary>
        /// Whether a payment made in a ledger whose parent closed at <paramref name="parentCloseTime"/>
        /// counts as late.
        /// </summary>
        /// <param name="loan">The loan entry.</param>
        /// <param name="parentCloseTime">The close time of the ledger before the one the payment lands in.</param>
        /// <param name="dueTimeIsLate">
        /// Whether a payment exactly at <c>NextPaymentDueDate</c> is late. rippled counted it late
        /// until <c>fixCleanup3_4_0</c>, and on time since.
        /// </param>
        public static bool IsPaymentLate(LOLoan loan, DateTime parentCloseTime, bool dueTimeIsLate)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));

            return loan.NextPaymentDueDate is { } due && HasPassed(parentCloseTime, due, dueTimeIsLate);
        }

        /// <summary>
        /// Whether the broker may default the loan (<c>LoanManage</c> with <c>tfLoanDefault</c>):
        /// the grace period after <c>NextPaymentDueDate</c> has run out.
        /// </summary>
        /// <param name="loan">The loan entry.</param>
        /// <param name="parentCloseTime">The close time of the ledger before the one the transaction lands in.</param>
        /// <param name="boundaryIsPast">
        /// Whether the exact end of the grace period already counts. rippled counted it until
        /// <c>fixCleanup3_4_0</c>, and not since.
        /// </param>
        public static bool IsPastGracePeriod(LOLoan loan, DateTime parentCloseTime, bool boundaryIsPast)
        {
            if (loan == null)
                throw new ArgumentNullException(nameof(loan));

            return loan.NextPaymentDueDate is { } due
                && HasPassed(parentCloseTime, due.AddSeconds(loan.GracePeriod ?? 0), boundaryIsPast);
        }

        /// <summary>XRP and MPT amounts are whole numbers of their unit; issued currencies are not.</summary>
        internal static bool IsIntegral(IssuedCurrency asset) => asset.IsXrp() || asset.MptIssuanceId != null;

        /// <summary>
        /// Rounds up to a whole unit for XRP and MPT, and to a multiple of 10^<paramref name="scale"/>
        /// for an issued currency, as rippled rounds a periodic payment.
        /// </summary>
        internal static decimal RoundUpToAsset(decimal value, bool integralAsset, int scale)
        {
            if (integralAsset)
                return Math.Ceiling(value);

            // Outside this range the step cannot be represented as a decimal; leave the value alone.
            if (scale is < -28 or > 28)
                return value;

            decimal step = scale >= 0 ? Pow10(scale) : 1m / Pow10(-scale);
            return Math.Ceiling(value / step) * step;
        }

        private static decimal Pow10(int exponent)
        {
            decimal result = 1m;
            for (int i = 0; i < exponent; i++)
                result *= 10m;

            return result;
        }

        private static bool HasPassed(DateTime now, DateTime boundary, bool inclusive) =>
            inclusive ? now >= boundary : now > boundary;
    }
}
