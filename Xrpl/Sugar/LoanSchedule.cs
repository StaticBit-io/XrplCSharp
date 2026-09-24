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
        private const uint TenthBipsPerUnity = 100_000;
        private const uint SecondsInYear = 365 * 24 * 60 * 60;

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

            if (loan.PeriodicPayment is not { } periodicPayment)
                throw new ArgumentException("The loan carries no PeriodicPayment.", nameof(loan));

            Terms terms = new Terms(
                options.Context,
                options.FixCleanup3_2_0,
                LoanPayments.IsIntegral(asset),
                loan.LoanScale ?? 0,
                periodicPayment,
                PeriodicRate(loan.InterestRate ?? 0, loan.PaymentInterval ?? 0, options.Context),
                managementFeeRate);

            XrplNumber roundedPeriodic = AssetRounding.Round(periodicPayment, terms.Integral, terms.Scale, terms.Context.WithRounding(NumberRounding.Upward));
            decimal serviceFee = ToDecimal(loan.LoanServiceFee ?? XrplNumber.Zero);

            LoanState state = new LoanState(
                loan.TotalValueOutstanding ?? XrplNumber.Zero,
                loan.PrincipalOutstanding ?? XrplNumber.Zero,
                loan.ManagementFeeOutstanding ?? XrplNumber.Zero,
                terms.Context);
            DateTime? dueDate = loan.NextPaymentDueDate;
            uint interval = loan.PaymentInterval ?? 0;

            List<LoanScheduleRow> rows = new List<LoanScheduleRow>((int)Math.Min(remaining, 10_000));
            for (int number = 1; remaining > 0; number++)
            {
                bool final = remaining == 1 || state.Value <= roundedPeriodic;
                Payment step = final
                    ? new Payment(state.Value, state.Principal, state.ManagementFee)
                    : Step(state, roundedPeriodic, remaining - 1, terms);

                state = final
                    ? LoanState.Cleared
                    : new LoanState(
                        XrplNumber.Subtract(state.Value, step.Value, terms.Context),
                        XrplNumber.Subtract(state.Principal, step.Principal, terms.Context),
                        XrplNumber.Subtract(state.ManagementFee, step.ManagementFee, terms.Context),
                        terms.Context);
                remaining = final ? 0 : remaining - 1;

                rows.Add(new LoanScheduleRow
                {
                    Number = number,
                    DueDate = dueDate,
                    Principal = ToDecimal(step.Principal),
                    Interest = ToDecimal(step.Interest(terms.Context)),
                    ManagementFee = ToDecimal(step.ManagementFee),
                    ServiceFee = serviceFee,
                    PrincipalOutstandingAfter = ToDecimal(state.Principal),
                    TotalValueOutstandingAfter = ToDecimal(state.Value),
                    IsFinal = final,
                });

                dueDate = dueDate?.AddSeconds(interval);
            }

            return rows;
        }

        /// <summary>rippled's <c>computePaymentComponents</c> for a payment that is not the last.</summary>
        private static Payment Step(LoanState current, XrplNumber roundedPeriodic, uint paymentsAfter, Terms terms)
        {
            NumberContext c = terms.Context;
            TheoreticalState target = TheoreticalState.After(terms, paymentsAfter);

            NumberContext principalRounding = terms.FixCleanup3_2_0 ? c.WithRounding(NumberRounding.Upward) : c;
            NumberContext interestRounding = terms.FixCleanup3_2_0 ? c.WithRounding(NumberRounding.Downward) : c;
            XrplNumber roundedPrincipal = AssetRounding.Round(target.Principal, terms.Integral, terms.Scale, principalRounding);
            XrplNumber roundedInterest = AssetRounding.Round(target.Interest, terms.Integral, terms.Scale, interestRounding);
            XrplNumber roundedFee = AssetRounding.Round(target.ManagementFee, terms.Integral, terms.Scale, c);

            XrplNumber principal = NonNegative(XrplNumber.Subtract(current.Principal, roundedPrincipal, c));
            XrplNumber interest = NonNegative(XrplNumber.Subtract(current.Interest, roundedInterest, c));
            XrplNumber fee = NonNegative(XrplNumber.Subtract(current.ManagementFee, roundedFee, c));

            principal = Min(principal, current.Principal);
            interest = Min(Min(interest, Max(XrplNumber.Zero, XrplNumber.Subtract(roundedPeriodic, principal, c))), current.Interest);
            fee = Min(Min(fee, XrplNumber.Subtract(roundedPeriodic, XrplNumber.Add(principal, interest, c), c)), current.ManagementFee);

            Deltas deltas = new Deltas(principal, interest, fee);

            XrplNumber overpayment = XrplNumber.Subtract(deltas.Total(c), current.Value, c);
            if (overpayment > XrplNumber.Zero)
                deltas = deltas.Reduce(overpayment, c);

            XrplNumber shortage = XrplNumber.Subtract(roundedPeriodic, deltas.Total(c), c);
            if (shortage < XrplNumber.Zero)
                deltas = deltas.Reduce(-shortage, c);

            return new Payment(
                Clamp(deltas.Total(c), current.Value),
                Clamp(deltas.Principal, current.Principal),
                Clamp(deltas.ManagementFee, current.ManagementFee));
        }

        /// <summary>
        /// <c>loanPeriodicRate</c>: <c>tenthBipsOfValue(Number(interval), rate) / kSecondsInYear</c>,
        /// equation (1) of XLS-66.
        /// </summary>
        private static XrplNumber PeriodicRate(uint interestRate, uint paymentInterval, NumberContext c) =>
            XrplNumber.Divide(TenthBipsOf((XrplNumber)(long)paymentInterval, interestRate, c), (XrplNumber)(long)SecondsInYear, c);

        /// <summary><c>tenthBipsOfValue</c>: <c>value * bips / 100000</c>.</summary>
        private static XrplNumber TenthBipsOf(XrplNumber value, uint tenthBips, NumberContext c) =>
            XrplNumber.Divide(XrplNumber.Multiply(value, (XrplNumber)(long)tenthBips, c), (XrplNumber)(long)TenthBipsPerUnity, c);

        /// <summary><c>loanPrincipalFromPeriodicPayment</c>, equation (10) of XLS-66.</summary>
        private static XrplNumber PrincipalFromPeriodicPayment(Terms terms, uint payments)
        {
            NumberContext c = terms.Context;
            if (payments == 0)
                return XrplNumber.Zero;

            if (terms.PeriodicRate.IsZero)
                return XrplNumber.Multiply(terms.PeriodicPayment, (XrplNumber)(long)payments, c);

            return XrplNumber.Divide(terms.PeriodicPayment, PaymentFactor(terms, payments), c);
        }

        /// <summary><c>computePaymentFactor</c>, equation (6) of XLS-66; <paramref name="payments"/> is not zero and the rate is not zero.</summary>
        private static XrplNumber PaymentFactor(Terms terms, uint payments)
        {
            NumberContext c = terms.Context;
            XrplNumber rate = terms.PeriodicRate;
            if (terms.FixCleanup3_2_0)
            {
                XrplNumber raisedMinusOne = PowerMinusOneHybrid(rate, payments, c);
                XrplNumber raised = XrplNumber.Add(1, raisedMinusOne, c);
                return XrplNumber.Divide(XrplNumber.Multiply(rate, raised, c), raisedMinusOne, c);
            }

            XrplNumber raisedRate = XrplNumber.Power(XrplNumber.Add(1, rate, c), payments, c);
            return XrplNumber.Divide(XrplNumber.Multiply(rate, raisedRate, c), XrplNumber.Subtract(raisedRate, 1, c), c);
        }

        /// <summary>
        /// <c>computePowerMinusOneHybrid</c>: <c>(1 + r)^n - 1</c> in closed form, or as a binomial
        /// sum when <c>n * r</c> is below 10^-9, where the closed form cancels.
        /// </summary>
        private static XrplNumber PowerMinusOneHybrid(XrplNumber rate, uint payments, NumberContext c)
        {
            if (payments == 0 || rate.IsZero)
                return XrplNumber.Zero;

            XrplNumber count = (XrplNumber)(long)payments;
            if (XrplNumber.Multiply(count, rate, c) >= new XrplNumber(1, -9))
                return XrplNumber.Subtract(XrplNumber.Power(XrplNumber.Add(1, rate, c), payments, c), 1, c);

            XrplNumber term = XrplNumber.Multiply(count, rate, c);
            XrplNumber sum = term;
            for (uint k = 1; k < payments; k++)
            {
                term = XrplNumber.Divide(
                    XrplNumber.Multiply(XrplNumber.Multiply(term, rate, c), (XrplNumber)(long)(payments - k), c),
                    (XrplNumber)(long)(k + 1),
                    c);
                XrplNumber next = XrplNumber.Add(sum, term, c);
                if (next == sum)
                    break;
                sum = next;
            }

            return sum;
        }

        private static XrplNumber NonNegative(XrplNumber value) => value < XrplNumber.Zero ? XrplNumber.Zero : value;

        private static XrplNumber Min(XrplNumber a, XrplNumber b) => b < a ? b : a;

        private static XrplNumber Max(XrplNumber a, XrplNumber b) => a < b ? b : a;

        /// <summary><c>std::clamp(value, 0, high)</c>.</summary>
        private static XrplNumber Clamp(XrplNumber value, XrplNumber high) =>
            value < XrplNumber.Zero ? XrplNumber.Zero : high < value ? high : value;

        private static decimal ToDecimal(XrplNumber number)
        {
            if (!number.TryToDecimal(out decimal value))
                throw new OverflowException($"{number} is outside the range of System.Decimal.");

            return value;
        }

        /// <summary>What stays fixed across the schedule.</summary>
        private sealed class Terms
        {
            public Terms(
                NumberContext context,
                bool fixCleanup3_2_0,
                bool integral,
                int scale,
                XrplNumber periodicPayment,
                XrplNumber periodicRate,
                ushort managementFeeRate)
            {
                Context = context;
                FixCleanup3_2_0 = fixCleanup3_2_0;
                Integral = integral;
                Scale = scale;
                PeriodicPayment = periodicPayment;
                PeriodicRate = periodicRate;
                ManagementFeeRate = managementFeeRate;
            }

            public NumberContext Context { get; }

            public bool FixCleanup3_2_0 { get; }

            public bool Integral { get; }

            public int Scale { get; }

            public XrplNumber PeriodicPayment { get; }

            public XrplNumber PeriodicRate { get; }

            public ushort ManagementFeeRate { get; }
        }

        /// <summary><c>constructLoanState</c>: the loan's tracked values, interest being what the other two leave of the value.</summary>
        private readonly struct LoanState
        {
            public LoanState(XrplNumber value, XrplNumber principal, XrplNumber managementFee, NumberContext c)
            {
                Value = value;
                Principal = principal;
                ManagementFee = managementFee;
                Interest = XrplNumber.Subtract(XrplNumber.Subtract(value, principal, c), managementFee, c);
            }

            public static LoanState Cleared => default;

            public XrplNumber Value { get; }

            public XrplNumber Principal { get; }

            public XrplNumber Interest { get; }

            public XrplNumber ManagementFee { get; }
        }

        /// <summary><c>computeTheoreticalLoanState</c>, equations (30) to (33) of XLS-66.</summary>
        private readonly struct TheoreticalState
        {
            private TheoreticalState(XrplNumber principal, XrplNumber interest, XrplNumber managementFee)
            {
                Principal = principal;
                Interest = interest;
                ManagementFee = managementFee;
            }

            public XrplNumber Principal { get; }

            public XrplNumber Interest { get; }

            public XrplNumber ManagementFee { get; }

            public static TheoreticalState After(Terms terms, uint paymentsAfter)
            {
                NumberContext c = terms.Context;
                XrplNumber value = XrplNumber.Multiply(terms.PeriodicPayment, (XrplNumber)(long)paymentsAfter, c);
                XrplNumber principal = PrincipalFromPeriodicPayment(terms, paymentsAfter);
                XrplNumber grossInterest = XrplNumber.Subtract(value, principal, c);
                XrplNumber fee = TenthBipsOf(grossInterest, terms.ManagementFeeRate, c);
                return new TheoreticalState(principal, XrplNumber.Subtract(grossInterest, fee, c), fee);
            }
        }

        /// <summary>
        /// <c>PaymentComponents</c>: the tracked value, principal and management fee a payment takes;
        /// its interest is what the other two leave of the value (<c>trackedInterestPart</c>).
        /// </summary>
        private readonly struct Payment
        {
            public Payment(XrplNumber value, XrplNumber principal, XrplNumber managementFee)
            {
                Value = value;
                Principal = principal;
                ManagementFee = managementFee;
            }

            public XrplNumber Value { get; }

            public XrplNumber Principal { get; }

            public XrplNumber ManagementFee { get; }

            public XrplNumber Interest(NumberContext c) =>
                XrplNumber.Subtract(Value, XrplNumber.Add(Principal, ManagementFee, c), c);
        }

        /// <summary><c>LoanStateDeltas</c>.</summary>
        private readonly struct Deltas
        {
            public Deltas(XrplNumber principal, XrplNumber interest, XrplNumber managementFee)
            {
                Principal = principal;
                Interest = interest;
                ManagementFee = managementFee;
            }

            public XrplNumber Principal { get; }

            public XrplNumber Interest { get; }

            public XrplNumber ManagementFee { get; }

            /// <summary><c>LoanStateDeltas::total</c>: principal + interest + managementFee.</summary>
            public XrplNumber Total(NumberContext c) =>
                XrplNumber.Add(XrplNumber.Add(Principal, Interest, c), ManagementFee, c);

            /// <summary>Takes <paramref name="excess"/> out of interest, then the management fee, then principal, as rippled does.</summary>
            public Deltas Reduce(XrplNumber excess, NumberContext c)
            {
                XrplNumber interest = Take(Interest, ref excess, c);
                XrplNumber fee = Take(ManagementFee, ref excess, c);
                XrplNumber principal = Take(Principal, ref excess, c);
                return new Deltas(principal, interest, fee);
            }

            private static XrplNumber Take(XrplNumber component, ref XrplNumber excess, NumberContext c)
            {
                if (excess <= XrplNumber.Zero)
                    return component;

                XrplNumber part = Min(component, excess);
                excess = XrplNumber.Subtract(excess, part, c);
                return XrplNumber.Subtract(component, part, c);
            }
        }
    }
}
