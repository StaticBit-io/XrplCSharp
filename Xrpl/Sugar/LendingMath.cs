using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// rippled's <c>LendingHelpers.cpp</c> in <see cref="XrplNumber"/>: every formula evaluated in
    /// the same order and rounded the same way, so the results are what the node computes.
    /// </summary>
    internal static class LendingMath
    {
        private const uint TenthBipsPerUnity = 100_000;
        private const uint SecondsInYear = 365 * 24 * 60 * 60;
        private static readonly DateTime RippleEpoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// <c>loanPeriodicRate</c>: <c>tenthBipsOfValue(Number(interval), rate) / kSecondsInYear</c>,
        /// equation (1) of XLS-66.
        /// </summary>
        internal static XrplNumber PeriodicRate(uint interestRate, long seconds, NumberContext c) =>
            XrplNumber.Divide(TenthBipsOf((XrplNumber)seconds, interestRate, c), (XrplNumber)(long)SecondsInYear, c);

        /// <summary><c>tenthBipsOfValue</c>: <c>value * bips / 100000</c>.</summary>
        internal static XrplNumber TenthBipsOf(XrplNumber value, uint tenthBips, NumberContext c) =>
            XrplNumber.Divide(XrplNumber.Multiply(value, (XrplNumber)(long)tenthBips, c), (XrplNumber)(long)TenthBipsPerUnity, c);

        /// <summary><c>loanPrincipalFromPeriodicPayment</c>, equation (10) of XLS-66.</summary>
        internal static XrplNumber PrincipalFromPeriodicPayment(LoanTerms terms, uint payments)
        {
            NumberContext c = terms.Context;
            if (payments == 0)
                return XrplNumber.Zero;

            if (terms.PeriodicRate.IsZero)
                return XrplNumber.Multiply(terms.PeriodicPayment, (XrplNumber)(long)payments, c);

            return XrplNumber.Divide(terms.PeriodicPayment, PaymentFactor(terms.PeriodicRate, payments, terms.FixCleanup3_2_0, c), c);
        }

        /// <summary><c>loanPeriodicPayment</c>, equation (7) of XLS-66: the payment that amortizes the principal.</summary>
        internal static XrplNumber PeriodicPayment(XrplNumber principal, XrplNumber periodicRate, uint payments, bool fixCleanup3_2_0, NumberContext c)
        {
            if (principal.IsZero || payments == 0)
                return XrplNumber.Zero;

            if (periodicRate.IsZero)
                return XrplNumber.Divide(principal, (XrplNumber)(long)payments, c);

            return XrplNumber.Multiply(principal, PaymentFactor(periodicRate, payments, fixCleanup3_2_0, c), c);
        }

        /// <summary>
        /// <c>computePaymentComponents</c>: the tracked parts of the next regular payment. The final
        /// payment - the last one, or one the rounded periodic payment covers - clears the loan.
        /// </summary>
        internal static LoanPaymentParts RegularPayment(LoanState current, uint paymentsRemaining, LoanTerms terms)
        {
            if (paymentsRemaining == 1 || current.Value <= terms.RoundedPeriodicPayment)
                return new LoanPaymentParts(current.Value, current.Principal, current.ManagementFee, isFinal: true);

            NumberContext c = terms.Context;
            TheoreticalState target = TheoreticalState.After(terms, paymentsRemaining - 1);

            NumberContext principalRounding = terms.FixCleanup3_2_0 ? c.WithRounding(NumberRounding.Upward) : c;
            NumberContext interestRounding = terms.FixCleanup3_2_0 ? c.WithRounding(NumberRounding.Downward) : c;
            XrplNumber roundedPrincipal = AssetRounding.Round(target.Principal, terms.Integral, terms.Scale, principalRounding);
            XrplNumber roundedInterest = AssetRounding.Round(target.Interest, terms.Integral, terms.Scale, interestRounding);
            XrplNumber roundedFee = AssetRounding.Round(target.ManagementFee, terms.Integral, terms.Scale, c);

            XrplNumber principal = NonNegative(XrplNumber.Subtract(current.Principal, roundedPrincipal, c));
            XrplNumber interest = NonNegative(XrplNumber.Subtract(current.Interest, roundedInterest, c));
            XrplNumber fee = NonNegative(XrplNumber.Subtract(current.ManagementFee, roundedFee, c));

            XrplNumber roundedPeriodic = terms.RoundedPeriodicPayment;
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

            return new LoanPaymentParts(
                Clamp(deltas.Total(c), current.Value),
                Clamp(deltas.Principal, current.Principal),
                Clamp(deltas.ManagementFee, current.ManagementFee),
                isFinal: false);
        }

        /// <summary>
        /// <c>loanLatePaymentInterest</c>, equation (16) of XLS-66: the principal times the late
        /// rate prorated over the seconds past the due date; zero when the payment is not late.
        /// </summary>
        internal static XrplNumber LatePaymentInterest(XrplNumber principalOutstanding, uint lateInterestRate, long now, long nextPaymentDueDate, NumberContext c)
        {
            if (principalOutstanding.IsZero || lateInterestRate == 0 || now <= nextPaymentDueDate)
                return XrplNumber.Zero;

            return XrplNumber.Multiply(principalOutstanding, PeriodicRate(lateInterestRate, now - nextPaymentDueDate, c), c);
        }

        /// <summary>
        /// <c>computeFullPaymentInterest</c>, equations (27) and (28) of XLS-66: interest accrued
        /// since the last payment (<c>loanAccruedInterest</c>) plus the prepayment penalty.
        /// </summary>
        internal static XrplNumber FullPaymentInterest(
            XrplNumber theoreticalPrincipal,
            XrplNumber periodicRate,
            long now,
            uint paymentInterval,
            long previousPaymentDate,
            long startDate,
            uint closeInterestRate,
            NumberContext c)
        {
            XrplNumber accrued = XrplNumber.Zero;
            long lastPaymentDate = Math.Max(previousPaymentDate, startDate);
            if (!periodicRate.IsZero && paymentInterval != 0 && now > lastPaymentDate)
            {
                // rippled multiplies first and divides last.
                accrued = XrplNumber.Divide(
                    XrplNumber.Multiply(XrplNumber.Multiply(theoreticalPrincipal, periodicRate, c), (XrplNumber)(now - lastPaymentDate), c),
                    (XrplNumber)(long)paymentInterval,
                    c);
            }

            XrplNumber penalty = closeInterestRate == 0 ? XrplNumber.Zero : TenthBipsOf(theoreticalPrincipal, closeInterestRate, c);
            return XrplNumber.Add(accrued, penalty, c);
        }

        /// <summary>
        /// <c>roundAndSplitInterest</c>: the interest rounded to the asset, then split into what the
        /// vault receives and the broker's management fee (<c>computeManagementFee</c>, rounded down).
        /// </summary>
        internal static (XrplNumber Interest, XrplNumber ManagementFee) RoundAndSplitInterest(
            XrplNumber rawInterest,
            LoanTerms terms,
            NumberRounding rounding)
        {
            NumberContext c = terms.Context;
            XrplNumber interest = AssetRounding.Round(rawInterest, terms.Integral, terms.Scale, c.WithRounding(rounding));
            XrplNumber fee = AssetRounding.Round(
                TenthBipsOf(interest, terms.ManagementFeeRate, c),
                terms.Integral,
                terms.Scale,
                c.WithRounding(NumberRounding.Downward));
            return (XrplNumber.Subtract(interest, fee, c), fee);
        }

        /// <summary>Seconds since the XRPL epoch; a local time is converted, an unspecified one is taken as UTC.</summary>
        internal static long RippleSeconds(DateTime time)
        {
            DateTime utc = time.Kind switch
            {
                DateTimeKind.Local => time.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
                _ => time,
            };
            return (long)Math.Floor((utc - RippleEpoch).TotalSeconds);
        }

        /// <summary><c>computePaymentFactor</c>, equation (6) of XLS-66; <paramref name="payments"/> is not zero and the rate is not zero.</summary>
        private static XrplNumber PaymentFactor(XrplNumber rate, uint payments, bool fixCleanup3_2_0, NumberContext c)
        {
            if (fixCleanup3_2_0)
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

            public static TheoreticalState After(LoanTerms terms, uint paymentsAfter)
            {
                NumberContext c = terms.Context;
                XrplNumber value = XrplNumber.Multiply(terms.PeriodicPayment, (XrplNumber)(long)paymentsAfter, c);
                XrplNumber principal = PrincipalFromPeriodicPayment(terms, paymentsAfter);
                XrplNumber grossInterest = XrplNumber.Subtract(value, principal, c);
                XrplNumber fee = TenthBipsOf(grossInterest, terms.ManagementFeeRate, c);
                return new TheoreticalState(principal, XrplNumber.Subtract(grossInterest, fee, c), fee);
            }
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

    /// <summary>What stays fixed for a loan's payments: its arithmetic, asset, scale and rates.</summary>
    internal sealed class LoanTerms
    {
        private LoanTerms(
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
            RoundedPeriodicPayment = AssetRounding.Round(periodicPayment, integral, scale, context.WithRounding(NumberRounding.Upward));
        }

        public NumberContext Context { get; }

        public bool FixCleanup3_2_0 { get; }

        public bool Integral { get; }

        public int Scale { get; }

        public XrplNumber PeriodicPayment { get; }

        /// <summary><c>roundPeriodicPayment</c>: the periodic payment rounded up to the asset.</summary>
        public XrplNumber RoundedPeriodicPayment { get; }

        public XrplNumber PeriodicRate { get; }

        public ushort ManagementFeeRate { get; }

        /// <summary>Terms from their parts.</summary>
        public static LoanTerms Create(
            NumberContext context,
            bool fixCleanup3_2_0,
            bool integral,
            int scale,
            XrplNumber periodicPayment,
            XrplNumber periodicRate,
            ushort managementFeeRate) =>
            new LoanTerms(context, fixCleanup3_2_0, integral, scale, periodicPayment, periodicRate, managementFeeRate);

        /// <summary>The terms of a <c>Loan</c> entry; the entry must carry <c>PeriodicPayment</c>.</summary>
        public static LoanTerms Of(LOLoan loan, IssuedCurrency asset, ushort managementFeeRate, LedgerRules options)
        {
            if (loan.PeriodicPayment is not { } periodicPayment)
                throw new ArgumentException("The loan carries no PeriodicPayment.", nameof(loan));

            NumberContext context = options.Context;
            return new LoanTerms(
                context,
                options.FixCleanup3_2_0,
                LoanPayments.IsIntegral(asset),
                loan.LoanScale ?? 0,
                periodicPayment,
                LendingMath.PeriodicRate(loan.InterestRate ?? 0, loan.PaymentInterval ?? 0, context),
                managementFeeRate);
        }
    }

    /// <summary><c>constructLoanState</c>: the loan's tracked values, interest being what the other two leave of the value.</summary>
    internal readonly struct LoanState
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

        /// <summary>The tracked values of a <c>Loan</c> entry.</summary>
        public static LoanState Of(LOLoan loan, NumberContext c) => new LoanState(
            loan.TotalValueOutstanding ?? XrplNumber.Zero,
            loan.PrincipalOutstanding ?? XrplNumber.Zero,
            loan.ManagementFeeOutstanding ?? XrplNumber.Zero,
            c);

        /// <summary>The state after a payment takes <paramref name="parts"/>.</summary>
        public LoanState After(LoanPaymentParts parts, NumberContext c) => parts.IsFinal
            ? Cleared
            : new LoanState(
                XrplNumber.Subtract(Value, parts.Value, c),
                XrplNumber.Subtract(Principal, parts.Principal, c),
                XrplNumber.Subtract(ManagementFee, parts.ManagementFee, c),
                c);
    }

    /// <summary>
    /// <c>PaymentComponents</c>: the tracked value, principal and management fee a payment takes;
    /// its interest is what the other two leave of the value (<c>trackedInterestPart</c>).
    /// </summary>
    internal readonly struct LoanPaymentParts
    {
        public LoanPaymentParts(XrplNumber value, XrplNumber principal, XrplNumber managementFee, bool isFinal)
        {
            Value = value;
            Principal = principal;
            ManagementFee = managementFee;
            IsFinal = isFinal;
        }

        public XrplNumber Value { get; }

        public XrplNumber Principal { get; }

        public XrplNumber ManagementFee { get; }

        public bool IsFinal { get; }

        public XrplNumber Interest(NumberContext c) =>
            XrplNumber.Subtract(Value, XrplNumber.Add(Principal, ManagementFee, c), c);
    }
}
