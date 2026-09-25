using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// The terms a <c>LoanSet</c> would give a loan, or why the node would refuse them: the result of
    /// <see cref="LoanOrigination.Compute"/>.
    /// </summary>
    public sealed class LoanOriginationTerms
    {
        /// <summary>
        /// The <c>Loan</c> entry as <c>LoanSet</c> would create it: <c>PeriodicPayment</c>,
        /// <c>TotalValueOutstanding</c>, <c>PrincipalOutstanding</c>, <c>ManagementFeeOutstanding</c>,
        /// <c>LoanScale</c>, the rates, fees and schedule fields. <c>StartDate</c> and
        /// <c>NextPaymentDueDate</c> are set only when a start date was given. null when refused.
        /// </summary>
        public LOLoan Loan { get; init; }

        /// <summary>
        /// The result the node would give instead of creating the loan, such as
        /// <c>tecPRECISION_LOSS</c> or <c>tecINSUFFICIENT_FUNDS</c>; null when these checks pass.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when these checks pass.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the terms pass the checks <see cref="LoanOrigination.Compute"/> repeats.</summary>
        public bool IsAccepted => Refusal == null;
    }

    /// <summary>
    /// The terms of a loan before <c>LoanSet</c>, computed offline from the transaction and the
    /// vault and broker entries, the way rippled's <c>LoanSet</c> and <c>computeLoanProperties</c>
    /// compute them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result feeds <see cref="LoanSchedule.Project"/> directly, so a loan's full schedule is
    /// known before anyone signs, without a node. <see cref="PreviewSugar.PreviewLoanSet"/> asks
    /// the node instead and covers every check.
    /// </para>
    /// <para>
    /// Repeated here are the checks that depend on the amounts: the vault's available assets
    /// (<c>tecINSUFFICIENT_FUNDS</c>), a value the asset cannot hold or that has more precision
    /// than the loan's scale, and the amortization guards (<c>tecPRECISION_LOSS</c>). The
    /// transaction's own validation, the vault's and broker's limits and cover, reserves and
    /// authorization are not.
    /// </para>
    /// </remarks>
    public static class LoanOrigination
    {
        private const uint DefaultPaymentTotal = 1;
        private const uint DefaultPaymentInterval = 60;
        private const uint DefaultGracePeriod = 60;

        /// <summary>
        /// Computes what <paramref name="loanSet"/> would create.
        /// </summary>
        /// <param name="loanSet">The transaction: <c>PrincipalRequested</c>, the rates, fees and schedule.</param>
        /// <param name="vault">The broker's vault, which gives the asset and the scale of its <c>AssetsTotal</c>.</param>
        /// <param name="broker">The <c>LoanBroker</c>, which gives the management fee rate.</param>
        /// <param name="startDate">
        /// The close time of the ledger the loan would be created in, which sets <c>StartDate</c> and
        /// the first due date; null leaves both unset.
        /// </param>
        /// <param name="options">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The transaction carries no <c>PrincipalRequested</c>, or the vault no asset.</exception>
        public static LoanOriginationTerms Compute(
            LoanSet loanSet,
            LOVault vault,
            LOLoanBroker broker,
            DateTime? startDate = null,
            LedgerRules options = null)
        {
            if (loanSet == null)
                throw new ArgumentNullException(nameof(loanSet));
            if (vault == null)
                throw new ArgumentNullException(nameof(vault));
            if (broker == null)
                throw new ArgumentNullException(nameof(broker));
            if (loanSet.PrincipalRequested is not { } principal)
                throw new ArgumentException("The LoanSet carries no PrincipalRequested.", nameof(loanSet));
            if (vault.Asset is not IssuedCurrency asset)
                throw new ArgumentException("The vault carries no Asset.", nameof(vault));

            options ??= new LedgerRules();
            NumberContext c = options.Context;
            bool integral = LoanPayments.IsIntegral(asset);

            // Preclaim: every value field must be an amount the asset can hold.
            foreach ((string name, XrplNumber? value) in ValueFields(loanSet))
            {
                if (value is { } v && !AssetRounding.IsRepresentable(v, integral, c))
                    return Refused("tecPRECISION_LOSS", $"{name} ({v}) cannot be represented as an amount of the asset.");
            }

            if ((vault.AssetsAvailable ?? XrplNumber.Zero) < principal)
                return Refused("tecINSUFFICIENT_FUNDS", "The vault has fewer assets available than the principal requested.");

            uint interestRate = loanSet.InterestRate ?? 0;
            uint interval = loanSet.PaymentInterval ?? DefaultPaymentInterval;
            uint paymentTotal = loanSet.PaymentTotal ?? DefaultPaymentTotal;
            ushort managementFeeRate = broker.ManagementFeeRate ?? 0;
            int vaultScale = AssetRounding.Scale(vault.AssetsTotal ?? XrplNumber.Zero, integral, c);

            LoanProperties properties = LendingMath.ComputeLoanProperties(
                principal,
                LendingMath.PeriodicRate(interestRate, interval, c),
                paymentTotal,
                managementFeeRate,
                integral,
                vaultScale,
                options);

            foreach ((string name, XrplNumber? value) in ValueFields(loanSet))
            {
                if (value is { } v && !AssetRounding.IsRounded(v, integral, properties.LoanScale, c))
                {
                    return Refused(
                        "tecPRECISION_LOSS",
                        $"{name} ({v}) has more precision than the loan's scale 10^{properties.LoanScale}, set by its total value {properties.Value}.");
                }
            }

            if (LendingMath.LoanGuards(principal, interestRate != 0, paymentTotal, properties.Value, properties, c) is { } guard)
                return Refused("tecPRECISION_LOSS", guard);

            if (properties.ManagementFee < XrplNumber.Zero || properties.Value <= XrplNumber.Zero || properties.PeriodicPayment <= XrplNumber.Zero)
                return Refused("tecINTERNAL", "The computed loan properties are invalid.");

            DateTime? start = startDate is { } s ? ToUtc(s) : null;
            return new LoanOriginationTerms
            {
                Loan = new LOLoan
                {
                    Flags = loanSet.Flags is { } flags && flags.HasFlag(LoanSetFlags.tfLoanOverpayment) ? LoanFlags.lsfLoanOverpayment : 0,
                    LoanBrokerID = loanSet.LoanBrokerID,
                    LoanScale = properties.LoanScale,
                    StartDate = start,
                    PaymentInterval = interval,
                    LoanOriginationFee = loanSet.LoanOriginationFee ?? XrplNumber.Zero,
                    LoanServiceFee = loanSet.LoanServiceFee ?? XrplNumber.Zero,
                    LatePaymentFee = loanSet.LatePaymentFee ?? XrplNumber.Zero,
                    ClosePaymentFee = loanSet.ClosePaymentFee ?? XrplNumber.Zero,
                    OverpaymentFee = loanSet.OverpaymentFee ?? 0,
                    InterestRate = interestRate,
                    LateInterestRate = loanSet.LateInterestRate ?? 0,
                    CloseInterestRate = loanSet.CloseInterestRate ?? 0,
                    OverpaymentInterestRate = loanSet.OverpaymentInterestRate ?? 0,
                    GracePeriod = loanSet.GracePeriod ?? DefaultGracePeriod,
                    PrincipalOutstanding = principal,
                    PeriodicPayment = properties.PeriodicPayment,
                    TotalValueOutstanding = properties.Value,
                    ManagementFeeOutstanding = properties.ManagementFee,
                    NextPaymentDueDate = start?.AddSeconds(interval),
                    PaymentRemaining = paymentTotal,
                },
            };
        }

        private static (string Name, XrplNumber? Value)[] ValueFields(LoanSet loanSet) => new[]
        {
            ("PrincipalRequested", loanSet.PrincipalRequested),
            ("LoanOriginationFee", loanSet.LoanOriginationFee),
            ("LoanServiceFee", loanSet.LoanServiceFee),
            ("LatePaymentFee", loanSet.LatePaymentFee),
            ("ClosePaymentFee", loanSet.ClosePaymentFee),
        };

        private static LoanOriginationTerms Refused(string result, string reason) =>
            new LoanOriginationTerms { Refusal = result, RefusalReason = reason };

        private static DateTime ToUtc(DateTime time) => time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
            _ => time,
        };
    }
}
