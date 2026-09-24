using System;
using System.Numerics;

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

            XrplNumber serviceFee = loan.LoanServiceFee ?? XrplNumber.Zero;
            if (loan.PaymentRemaining == 1)
                return loan.TotalValueOutstanding is { } total ? TryAdd(total, serviceFee) : null;

            if (loan.PeriodicPayment is not { } periodic
                || RoundUpToAsset(periodic, IsIntegral(asset), loan.LoanScale ?? 0) is not { } payment)
                return null;

            return TryAdd(payment, serviceFee);
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
        /// for an issued currency, as rippled rounds a periodic payment. The rounding is exact - done
        /// on the value itself, before it becomes a <see cref="decimal"/> - so nothing the conversion
        /// drops can make the result smaller than the value.
        /// </summary>
        /// <returns>null when the rounded value is beyond <see cref="decimal"/>.</returns>
        internal static decimal? RoundUpToAsset(XrplNumber value, bool integralAsset, int scale)
        {
            // A step below 10^-28 is not a decimal; rounding up to 10^-28 instead stays an upper bound.
            int step = integralAsset ? 0 : Math.Max(scale, -28);
            return CeilingToPowerOfTen(value, step).TryToDecimal(out decimal rounded) ? rounded : null;
        }

        /// <summary>The smallest multiple of 10^<paramref name="exponent"/> not below a positive value.</summary>
        internal static XrplNumber CeilingToPowerOfTen(XrplNumber value, int exponent)
        {
            if (value.Sign <= 0 || value.Exponent >= exponent)
                return value;

            // A mantissa has at most 19 digits: dropping more than 19 leaves nothing but the carry.
            int dropped = (int)Math.Min((long)exponent - value.Exponent, 20);
            BigInteger quotient = BigInteger.DivRem(value.Mantissa, BigInteger.Pow(10, dropped), out BigInteger remainder);
            if (!remainder.IsZero)
                quotient += 1;

            return new XrplNumber((long)quotient, exponent);
        }

        private static decimal? TryAdd(XrplNumber left, XrplNumber right) =>
            left.TryToDecimal(out decimal a) ? TryAdd(a, right) : null;

        private static decimal? TryAdd(decimal left, XrplNumber right)
        {
            if (!right.TryToDecimal(out decimal b))
                return null;

            try
            {
                return left + b;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        private static bool HasPassed(DateTime now, DateTime boundary, bool inclusive) =>
            inclusive ? now >= boundary : now > boundary;
    }
}
