using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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

        /// <summary>Reads the amendments from the node.</summary>
        public static async Task<LoanScheduleOptions> FromNodeAsync(IXrplClient client, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            ServerFeatures features = await client
                .ServerFeatures("fixCleanup3_2_0", cancellationToken)
                .Typed()
                .ConfigureAwait(false);

            return new LoanScheduleOptions
            {
                FixCleanup3_2_0 = features.GetByName("fixCleanup3_2_0") is { } feature && feature.Value.Enabled,
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
    /// The arithmetic is <see cref="decimal"/>, 28 significant digits against the 19 of rippled's
    /// <c>Number</c>. A payment part can come out one rounding unit off only when an exact value
    /// sits on a rounding boundary, which the node's own arithmetic may round the other way. Late,
    /// full and overpayments are not projected.
    /// </para>
    /// </remarks>
    public static class LoanSchedule
    {
        private const decimal TenthBipsPerUnity = 100_000m;
        private const decimal SecondsInYear = 365m * 24 * 60 * 60;
        private const decimal CancellationThreshold = 0.000000001m;

        /// <summary>
        /// Projects the payments the loan still has to make.
        /// </summary>
        /// <param name="loan">The <c>Loan</c> entry, as the ledger or a <c>LoanSet</c> preview holds it.</param>
        /// <param name="asset">The loan asset: the asset of the broker's vault.</param>
        /// <param name="managementFeeRate">The broker's <c>ManagementFeeRate</c>, in 1/10th of a basis point.</param>
        /// <param name="options">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The entry lacks a field the projection needs.</exception>
        /// <exception cref="OverflowException">A value is beyond <see cref="decimal"/>.</exception>
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

            if (loan.PeriodicPayment is not { } periodicNumber)
                throw new ArgumentException("The loan carries no PeriodicPayment.", nameof(loan));

            bool integral = LoanPayments.IsIntegral(asset);
            int scale = loan.LoanScale ?? 0;
            decimal periodicPayment = ToDecimal(periodicNumber);
            decimal roundedPeriodic = LoanPayments.RoundUpToAsset(periodicNumber, integral, scale)
                ?? throw new OverflowException("PeriodicPayment is beyond System.Decimal once rounded.");
            decimal periodicRate = (loan.InterestRate ?? 0) / TenthBipsPerUnity * (loan.PaymentInterval ?? 0) / SecondsInYear;
            decimal feeRate = managementFeeRate / TenthBipsPerUnity;
            decimal serviceFee = ToDecimal(loan.LoanServiceFee ?? XrplNumber.Zero);

            decimal value = ToDecimal(loan.TotalValueOutstanding ?? XrplNumber.Zero);
            decimal principal = ToDecimal(loan.PrincipalOutstanding ?? XrplNumber.Zero);
            decimal managementFee = ToDecimal(loan.ManagementFeeOutstanding ?? XrplNumber.Zero);
            DateTime? dueDate = loan.NextPaymentDueDate;
            uint interval = loan.PaymentInterval ?? 0;

            List<LoanScheduleRow> rows = new List<LoanScheduleRow>((int)Math.Min(remaining, 10_000));
            for (int number = 1; remaining > 0; number++)
            {
                Deltas step = remaining == 1 || value <= roundedPeriodic
                    ? new Deltas(principal, value - principal - managementFee, managementFee)
                    : Step(value, principal, managementFee, roundedPeriodic, periodicPayment, periodicRate, remaining - 1, feeRate, integral, scale, options);

                bool final = remaining == 1 || value <= roundedPeriodic;
                value -= step.Total;
                principal -= step.Principal;
                managementFee -= step.ManagementFee;
                remaining = final ? 0 : remaining - 1;

                rows.Add(new LoanScheduleRow
                {
                    Number = number,
                    DueDate = dueDate,
                    Principal = step.Principal,
                    Interest = step.Interest,
                    ManagementFee = step.ManagementFee,
                    ServiceFee = serviceFee,
                    PrincipalOutstandingAfter = final ? 0 : principal,
                    TotalValueOutstandingAfter = final ? 0 : value,
                    IsFinal = final,
                });

                dueDate = dueDate?.AddSeconds(interval);
            }

            return rows;
        }

        /// <summary>rippled's <c>computePaymentComponents</c> for a payment that is not the last.</summary>
        private static Deltas Step(
            decimal value,
            decimal principal,
            decimal managementFee,
            decimal roundedPeriodic,
            decimal periodicPayment,
            decimal periodicRate,
            uint paymentsAfter,
            decimal feeRate,
            bool integral,
            int scale,
            LoanScheduleOptions options)
        {
            // computeTheoreticalLoanState for the state after this payment.
            decimal targetValue = periodicPayment * paymentsAfter;
            decimal targetPrincipal = PrincipalFromPeriodicPayment(periodicPayment, periodicRate, paymentsAfter, options);
            decimal grossInterest = targetValue - targetPrincipal;
            decimal targetFee = grossInterest * feeRate;
            decimal targetInterest = grossInterest - targetFee;

            Rounding principalRounding = options.FixCleanup3_2_0 ? Rounding.Upward : Rounding.ToNearest;
            Rounding interestRounding = options.FixCleanup3_2_0 ? Rounding.Downward : Rounding.ToNearest;
            decimal roundedPrincipal = RoundToAsset(targetPrincipal, integral, scale, principalRounding);
            decimal roundedInterest = RoundToAsset(targetInterest, integral, scale, interestRounding);
            decimal roundedFee = RoundToAsset(targetFee, integral, scale, Rounding.ToNearest);

            decimal interestDue = value - principal - managementFee;
            decimal principalDelta = Math.Min(Math.Max(principal - roundedPrincipal, 0m), principal);
            decimal interestDelta = Math.Min(
                Math.Min(Math.Max(interestDue - roundedInterest, 0m), Math.Max(0m, roundedPeriodic - principalDelta)),
                interestDue);
            decimal feeDelta = Math.Min(
                Math.Min(Math.Max(managementFee - roundedFee, 0m), roundedPeriodic - (principalDelta + interestDelta)),
                managementFee);

            Deltas deltas = new Deltas(principalDelta, interestDelta, feeDelta);

            decimal overpayment = deltas.Total - value;
            if (overpayment > 0)
                deltas = deltas.Reduce(overpayment);

            decimal shortage = roundedPeriodic - deltas.Total;
            if (shortage < 0)
                deltas = deltas.Reduce(-shortage);

            // The final clamp works on the value, principal and fee; interest is what is left of the value.
            decimal valueDelta = Math.Min(Math.Max(deltas.Total, 0m), value);
            decimal clampedPrincipal = Math.Min(Math.Max(deltas.Principal, 0m), principal);
            decimal clampedFee = Math.Min(Math.Max(deltas.ManagementFee, 0m), managementFee);
            return new Deltas(clampedPrincipal, valueDelta - clampedPrincipal - clampedFee, clampedFee);
        }

        /// <summary>Equation (10) of XLS-66: the principal a remaining schedule amortizes.</summary>
        private static decimal PrincipalFromPeriodicPayment(decimal periodicPayment, decimal rate, uint payments, LoanScheduleOptions options)
        {
            if (payments == 0)
                return 0m;

            if (rate == 0)
                return periodicPayment * payments;

            decimal raisedMinusOne = options.FixCleanup3_2_0 && payments * rate < CancellationThreshold
                ? PowerMinusOneBinomial(rate, payments)
                : Power(1m + rate, payments) - 1m;
            decimal factor = rate * (1m + raisedMinusOne) / raisedMinusOne;
            return periodicPayment / factor;
        }

        private static decimal Power(decimal value, uint exponent)
        {
            decimal result = 1m;
            decimal factor = value;
            while (exponent > 0)
            {
                if ((exponent & 1) == 1)
                    result *= factor;
                exponent >>= 1;
                if (exponent > 0)
                    factor *= factor;
            }

            return result;
        }

        /// <summary>(1 + r)^n - 1 as the sum of its binomial terms, which has no cancellation for tiny r.</summary>
        private static decimal PowerMinusOneBinomial(decimal rate, uint payments)
        {
            decimal term = payments * rate;
            decimal sum = term;
            for (uint k = 1; k < payments; k++)
            {
                term = term * rate * (payments - k) / (k + 1);
                decimal next = sum + term;
                if (next == sum)
                    break;
                sum = next;
            }

            return sum;
        }

        private enum Rounding
        {
            ToNearest,
            Upward,
            Downward,
        }

        /// <summary>
        /// rippled's <c>roundToAsset</c>: whole units for XRP and MPT; for an issued currency the
        /// 16 significant digits of an IOU amount first, then a multiple of 10^<paramref name="scale"/>.
        /// </summary>
        private static decimal RoundToAsset(decimal value, bool integral, int scale, Rounding mode)
        {
            if (integral)
                return RoundToPowerOfTen(value, 0, mode);

            if (value == 0)
                return 0m;

            decimal significant = RoundToPowerOfTen(value, Magnitude(value) - 15, mode);
            return RoundToPowerOfTen(significant, scale, mode);
        }

        /// <summary>The exponent of the leading digit: 2 for 345.6, -3 for 0.00123.</summary>
        private static int Magnitude(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            int decimalScale = (bits[3] >> 16) & 0xFF;
            BigInteger mantissa = (new BigInteger((uint)bits[2]) << 64)
                | (new BigInteger((uint)bits[1]) << 32)
                | new BigInteger((uint)bits[0]);
            int digits = mantissa.ToString(CultureInfo.InvariantCulture).Length;
            return digits - 1 - decimalScale;
        }

        /// <summary>Rounds to a multiple of 10^<paramref name="exponent"/>.</summary>
        private static decimal RoundToPowerOfTen(decimal value, int exponent, Rounding mode)
        {
            MidpointRounding rounding = mode switch
            {
                Rounding.Upward => MidpointRounding.ToPositiveInfinity,
                Rounding.Downward => MidpointRounding.ToNegativeInfinity,
                _ => MidpointRounding.ToEven,
            };

            if (exponent <= 0)
                return exponent < -28 ? value : decimal.Round(value, -exponent, rounding);

            decimal step = 1m;
            for (int i = 0; i < exponent && step < 1e27m; i++)
                step *= 10m;

            return decimal.Round(value / step, 0, rounding) * step;
        }

        private static decimal ToDecimal(XrplNumber number)
        {
            if (!number.TryToDecimal(out decimal value))
                throw new OverflowException($"{number} is outside the range of System.Decimal.");

            return value;
        }

        private readonly struct Deltas
        {
            public Deltas(decimal principal, decimal interest, decimal managementFee)
            {
                Principal = principal;
                Interest = interest;
                ManagementFee = managementFee;
            }

            public decimal Principal { get; }

            public decimal Interest { get; }

            public decimal ManagementFee { get; }

            public decimal Total => Principal + Interest + ManagementFee;

            /// <summary>Takes <paramref name="excess"/> out of interest, then the management fee, then principal, as rippled does.</summary>
            public Deltas Reduce(decimal excess)
            {
                decimal interest = Take(Interest, ref excess);
                decimal fee = Take(ManagementFee, ref excess);
                decimal principal = Take(Principal, ref excess);
                return new Deltas(principal, interest, fee);
            }

            private static decimal Take(decimal component, ref decimal excess)
            {
                if (excess <= 0)
                    return component;

                decimal part = Math.Min(component, excess);
                excess -= part;
                return component - part;
            }
        }
    }
}
