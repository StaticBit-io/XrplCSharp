using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// A preview through <c>simulate</c> must read exactly what the same transaction does once
/// submitted. Each test previews, submits the previewed transaction unchanged, and compares the
/// two outcomes field by field.
/// </summary>
[TestClass]
[TestCategory("Loan")]
public class TestIPreviewLoanVault : TestILoanBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestPreviewLoanSet_MatchesTheLoanTheSignedTransactionCreates()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);
        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet Terms() => new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 10_000_000,
            InterestRate = 25_000,
            PaymentTotal = 4,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 100,
            LoanOriginationFee = 1_000,
        };

        // No signature from either party: the borrower has not seen the terms yet.
        TransactionPreview<LoanSetOutcome> preview = await client.PreviewLoanSet(Terms());
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);
        LoanSetOutcome expected = preview.Outcome;

        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, Terms(), walletBroker, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome actual = LoanSetOutcome.FromMetadata(Terms(), created.Meta);

        Assert.AreEqual(expected.LoanId, actual.LoanId, "the loan ID follows the broker's LoanSequence");
        Assert.AreEqual(expected.Loan.PeriodicPayment, actual.Loan.PeriodicPayment);
        Assert.AreEqual(expected.Loan.TotalValueOutstanding, actual.Loan.TotalValueOutstanding);
        Assert.AreEqual(expected.Loan.PrincipalOutstanding, actual.Loan.PrincipalOutstanding);
        Assert.AreEqual(expected.Loan.ManagementFeeOutstanding, actual.Loan.ManagementFeeOutstanding);
        Assert.AreEqual(expected.Loan.PaymentRemaining, actual.Loan.PaymentRemaining);
        Assert.AreEqual(expected.BorrowerReceives, actual.BorrowerReceives);
        Assert.AreEqual(expected.TotalToRepay, actual.TotalToRepay);
        Assert.AreEqual(9_999_000m, actual.BorrowerReceives);
    }

    [TestMethod]
    public async Task TestLoanSchedule_MatchesEveryPaymentTheNodeTakes()
    {
        // A broker taking a 10% management fee, a loan at 100% a year so every period carries
        // interest worth splitting. The schedule is projected once, from the loan as created, and
        // every payment the node then takes is compared with its row.
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);
        const ushort managementFeeRate = 10_000;
        string brokerId = await CreateBrokerWithManagementFee(walletBroker, managementFeeRate);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 50_000_000,
            InterestRate = 100_000,
            PaymentTotal = 6,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 10,
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);

        LedgerRules options = await LedgerRules.FromNodeAsync(client);
        IReadOnlyList<LoanScheduleRow> schedule = LoanSchedule.Project(loanSet.Loan, loanSet.Asset, managementFeeRate, options);
        Assert.HasCount(6, schedule);

        await PayThroughAndCompare(schedule, loanSet, walletBorrower, cap => new Currency { Value = cap, CurrencyCode = "XRP" });

        Assert.IsTrue(schedule.Sum(r => r.ManagementFee) > 0, "the fee split was exercised");
    }

    [TestMethod]
    public async Task TestLoanSchedule_Mpt_MatchesEveryPaymentTheNodeTakes()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder, walletBorrower);
        (string brokerId, string mptIssuanceId) = await CreateMptBroker(client, walletIssuer, walletHolder);

        MPTokenAuthorize authorize = await client.Autofill(new MPTokenAuthorize
        {
            Account = walletBorrower.ClassicAddress,
            MPTokenIssuanceID = mptIssuanceId,
        });
        ValidateResult(await client.SubmitAndWait(authorize, walletBorrower, true));

        LoanSet loanTx = new LoanSet
        {
            Account = walletIssuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 97,
            InterestRate = 100_000,
            PaymentTotal = 5,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 1,
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletIssuer, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);

        // The borrower needs more than it borrowed to pay interest and fees.
        Payment topUp = await client.Autofill(new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletBorrower.ClassicAddress,
            Amount = new Currency { Value = "20", MPTokenIssuanceID = mptIssuanceId },
        });
        ValidateResult(await client.SubmitAndWait(topUp, walletIssuer, true));

        IReadOnlyList<LoanScheduleRow> schedule = LoanSchedule.Project(
            loanSet.Loan, loanSet.Asset, managementFeeRate: 0, await LedgerRules.FromNodeAsync(client));

        await PayThroughAndCompare(schedule, loanSet, walletBorrower, cap => new Currency { Value = cap, MPTokenIssuanceID = mptIssuanceId });
    }

    [TestMethod]
    [DataRow("1234.567", 7u, 100_000u, (ushort)10_000, "0.01")]
    [DataRow("100", 3u, 55_555u, (ushort)2_500, "0")]
    [DataRow("987654.321", 12u, 12_345u, (ushort)1_234, "0.5")]
    public async Task TestLoanSchedule_Iou_MatchesEveryPaymentTheNodeTakes(
        string principal,
        uint payments,
        uint interestRate,
        ushort managementFeeRate,
        string serviceFee)
    {
        // An issued currency is rounded to 16 significant digits and then to the loan's scale, the
        // path XRP and MPT never take.
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder, walletBorrower);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress };
        string brokerId = await CreateIouBroker(walletIssuer, walletHolder, walletBorrower, usd, managementFeeRate);

        LoanSet loanTx = new LoanSet
        {
            Account = walletIssuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = XrplNumber.Parse(principal),
            InterestRate = interestRate,
            PaymentTotal = payments,
            PaymentInterval = 60,
            GracePeriod = 60,
            LoanServiceFee = XrplNumber.Parse(serviceFee),
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletIssuer, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);

        IReadOnlyList<LoanScheduleRow> schedule = LoanSchedule.Project(
            loanSet.Loan, loanSet.Asset, managementFeeRate, await LedgerRules.FromNodeAsync(client));
        Assert.HasCount((int)payments, schedule);

        await PayThroughAndCompare(
            schedule,
            loanSet,
            walletBorrower,
            cap => new Currency { Value = cap, CurrencyCode = usd.Currency, Issuer = usd.Issuer });
    }

    [TestMethod]
    public async Task TestLatePaymentDue_MatchesWhatTheNodeCharges()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);
        const ushort managementFeeRate = 10_000;
        string brokerId = await CreateBrokerWithManagementFee(walletBroker, managementFeeRate);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 30_000_000,
            InterestRate = 80_000,
            LateInterestRate = 90_000,
            LatePaymentFee = 777,
            PaymentTotal = 3,
            PaymentInterval = 60,
            GracePeriod = 60,
            LoanServiceFee = 10,
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);

        LOLoan loan = await ReadLoan(loanSet.LoanId);
        await IntegrationTestConfig.WaitForCloseTimeAsync(client, loan.NextPaymentDueDate.Value.AddSeconds(5), nodeType);

        TimedPayment paid = await PayWithFlag(loanSet, walletBorrower, LoanPayFlags.tfLoanLatePayment, "40000000", amount => new Currency { Value = amount, CurrencyCode = "XRP" });
        LoanPaymentDue expected = LoanPayments.LatePaymentDue(
            loan, loanSet.Asset, managementFeeRate, paid.ParentCloseTime, await LedgerRules.FromNodeAsync(client));

        Assert.IsNotNull(expected, "the payment landed after the due date");
        AssertSame(expected, paid.Outcome);
        Assert.IsTrue(expected.PaidToBroker > 777m, "the late fee and the late interest's management fee reach the broker");
    }

    [TestMethod]
    public async Task TestFullPaymentDue_MatchesWhatTheNodeCharges()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);
        const ushort managementFeeRate = 7_500;
        string brokerId = await CreateBrokerWithManagementFee(walletBroker, managementFeeRate);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 40_000_000,
            InterestRate = 60_000,
            CloseInterestRate = 20_000,
            ClosePaymentFee = 333,
            PaymentTotal = 6,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 10,
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);

        // One regular payment first, so the accrued interest runs from a previous due date.
        await PayWithFlag(loanSet, walletBorrower, null, LoanPayments.RegularPaymentCap(await ReadLoan(loanSet.LoanId), loanSet.Asset).Value.ToString(CultureInfo.InvariantCulture), amount => new Currency { Value = amount, CurrencyCode = "XRP" });

        LOLoan loan = await ReadLoan(loanSet.LoanId);
        TimedPayment paid = await PayWithFlag(loanSet, walletBorrower, LoanPayFlags.tfLoanFullPayment, "60000000", amount => new Currency { Value = amount, CurrencyCode = "XRP" });
        LoanPaymentDue expected = LoanPayments.FullPaymentDue(
            loan, loanSet.Asset, managementFeeRate, paid.ParentCloseTime, await LedgerRules.FromNodeAsync(client));

        Assert.IsNotNull(expected);
        AssertSame(expected, paid.Outcome);
        Assert.IsTrue(paid.Outcome.IsPaidOff);
    }

    [TestMethod]
    public async Task TestFullPaymentDue_Iou_MatchesWhatTheNodeCharges()
    {
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder, walletBorrower);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress };
        const ushort managementFeeRate = 1_234;
        string brokerId = await CreateIouBroker(walletIssuer, walletHolder, walletBorrower, usd, managementFeeRate);
        Func<string, Currency> amount = value => new Currency { Value = value, CurrencyCode = usd.Currency, Issuer = usd.Issuer };

        LoanSet loanTx = new LoanSet
        {
            Account = walletIssuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = XrplNumber.Parse("4321.987"),
            InterestRate = 45_678,
            CloseInterestRate = 12_345,
            ClosePaymentFee = XrplNumber.Parse("1.25"),
            PaymentTotal = 8,
            PaymentInterval = 60,
            GracePeriod = 60,
            LoanServiceFee = XrplNumber.Parse("0.05"),
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletIssuer, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);

        await PayWithFlag(loanSet, walletBorrower, null, LoanPayments.RegularPaymentCap(await ReadLoan(loanSet.LoanId), usd).Value.ToString(CultureInfo.InvariantCulture), amount);

        LOLoan loan = await ReadLoan(loanSet.LoanId);
        TimedPayment paid = await PayWithFlag(loanSet, walletBorrower, LoanPayFlags.tfLoanFullPayment, "10000", amount);
        LoanPaymentDue expected = LoanPayments.FullPaymentDue(
            loan, loanSet.Asset, managementFeeRate, paid.ParentCloseTime, await LedgerRules.FromNodeAsync(client));

        Assert.IsNotNull(expected);
        AssertSame(expected, paid.Outcome);
    }

    [TestMethod]
    public async Task TestPaymentForAmount_MatchesWhatTheNodeTakes()
    {
        // Several regular payments in one LoanPay, then regular payments with an overpayment on
        // top, and the amounts the node refuses.
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);
        const ushort managementFeeRate = 10_000;
        string brokerId = await CreateBrokerWithManagementFee(walletBroker, managementFeeRate);
        Func<string, Currency> xrp = value => new Currency { Value = value, CurrencyCode = "XRP" };

        LoanSet Terms(LoanSetFlags? flags) => new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 30_000_000,
            InterestRate = 80_000,
            OverpaymentFee = 5_000,
            OverpaymentInterestRate = 20_000,
            PaymentTotal = 10,
            PaymentInterval = 60,
            GracePeriod = 60,
            LoanServiceFee = 10,
            Flags = flags,
        };
        LoanSet loanTx = Terms(LoanSetFlags.tfLoanOverpayment);
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);

        LOLoan loan = await ReadLoan(loanSet.LoanId);
        decimal cap = LoanPayments.RegularPaymentCap(loan, loanSet.Asset).Value;

        string threeAndAHalf = (cap * 3.5m).ToString("0", CultureInfo.InvariantCulture);
        TimedPayment paid = await PayWithFlag(loanSet, walletBorrower, null, threeAndAHalf, xrp);
        LoanAmountPayment expected = LoanPayments.PaymentForAmount(
            loan, loanSet.Asset, managementFeeRate, XrplNumber.Parse(threeAndAHalf), paid.ParentCloseTime, options: rules);
        AssertSame(expected, paid.Outcome);
        Assert.AreEqual(3u, expected.PaymentsMade);
        Assert.IsFalse(expected.IsOverpaid, "without tfLoanOverpayment the rest stays with the payer");

        loan = await ReadLoan(loanSet.LoanId);
        string twoAndAHalf = (cap * 2.5m).ToString("0", CultureInfo.InvariantCulture);
        paid = await PayWithFlag(loanSet, walletBorrower, LoanPayFlags.tfLoanOverpayment, twoAndAHalf, xrp);
        expected = LoanPayments.PaymentForAmount(
            loan, loanSet.Asset, managementFeeRate, XrplNumber.Parse(twoAndAHalf), paid.ParentCloseTime, overpayment: true, options: rules);
        AssertSame(expected, paid.Outcome);
        Assert.AreEqual(2u, expected.PaymentsMade);
        Assert.IsTrue(expected.IsOverpaid);
        Assert.AreNotEqual(loan.PeriodicPayment, expected.LoanAfter.PeriodicPayment, "the overpayment re-amortizes the loan");

        // Refusals: an amount short of one payment, and an overpayment the loan does not allow.
        loan = await ReadLoan(loanSet.LoanId);
        await AssertRefused(loanSet.LoanId, loan, loanSet.Asset, managementFeeRate, walletBorrower, "1", null, rules);

        LoanSet plainTx = Terms(null);
        TransactionSummary plainCreated = await SubmitLoanSetWithCounterpartySig(client, plainTx, walletBroker, walletBorrower);
        ValidateResult(plainCreated);
        string plainId = LoanSetOutcome.FromMetadata(plainTx, plainCreated.Meta).LoanId;
        await AssertRefused(plainId, await ReadLoan(plainId), loanSet.Asset, managementFeeRate, walletBorrower, threeAndAHalf, LoanPayFlags.tfLoanOverpayment, rules);
    }

    [TestMethod]
    public async Task TestPaymentForAmount_Iou_Overpayment_MatchesWhatTheNodeTakes()
    {
        // An issued currency: 16 significant digits, rounded to the loan's scale at every step.
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder, walletBorrower);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress };
        const ushort managementFeeRate = 1_234;
        string brokerId = await CreateIouBroker(walletIssuer, walletHolder, walletBorrower, usd, managementFeeRate);
        Func<string, Currency> amount = value => new Currency { Value = value, CurrencyCode = usd.Currency, Issuer = usd.Issuer };

        LoanSet loanTx = new LoanSet
        {
            Account = walletIssuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = XrplNumber.Parse("4321.987"),
            InterestRate = 45_678,
            OverpaymentFee = 1_000,
            OverpaymentInterestRate = 3_000,
            PaymentTotal = 8,
            PaymentInterval = 60,
            GracePeriod = 60,
            LoanServiceFee = XrplNumber.Parse("0.05"),
            Flags = LoanSetFlags.tfLoanOverpayment,
        };
        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletIssuer, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome loanSet = LoanSetOutcome.FromMetadata(loanTx, created.Meta);
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);

        LOLoan loan = await ReadLoan(loanSet.LoanId);
        decimal cap = LoanPayments.RegularPaymentCap(loan, usd).Value;
        string value = decimal.Round(cap * 2.5m, 10).ToString(CultureInfo.InvariantCulture);

        TimedPayment paid = await PayWithFlag(loanSet, walletBorrower, LoanPayFlags.tfLoanOverpayment, value, amount);
        LoanAmountPayment expected = LoanPayments.PaymentForAmount(
            loan, usd, managementFeeRate, XrplNumber.Parse(value), paid.ParentCloseTime, overpayment: true, options: rules);

        AssertSame(expected, paid.Outcome);
        Assert.AreEqual(2u, expected.PaymentsMade);
        Assert.IsTrue(expected.IsOverpaid);
    }

    [TestMethod]
    public async Task TestLoanOrigination_MatchesWhatTheNodeWouldCreate()
    {
        // Offline terms against the node's own LoanSet, previewed through simulate, for XRP and
        // USD loans across rates, sizes and a zero rate, and for terms the node refuses.
        XrplWallet walletXrpBroker = XrplWallet.Generate();
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletXrpBroker, walletIssuer, walletHolder, walletBorrower);
        string xrpBrokerId = await CreateBrokerWithManagementFee(walletXrpBroker, 10_000);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress };
        string usdBrokerId = await CreateIouBroker(walletIssuer, walletHolder, walletBorrower, usd, 1_234);
        LedgerRules options = await LedgerRules.FromNodeAsync(client);

        const string precisionLoss = "tecPRECISION_LOSS";
        (string Broker, XrplWallet Owner, string Principal, uint Rate, uint Payments, string ServiceFee, string Refusal)[] cases =
        {
            (xrpBrokerId, walletXrpBroker, "50000000", 100_000, 6, "10", null),
            (xrpBrokerId, walletXrpBroker, "1234567", 17_500, 12, "0", null),
            (xrpBrokerId, walletXrpBroker, "9000000", 0, 4, "3", null),
            // One drop a period settles the total value in 8 payments, not 12.
            (xrpBrokerId, walletXrpBroker, "7", 1_000, 12, "0", precisionLoss),
            // Half a drop is not an XRP amount.
            (xrpBrokerId, walletXrpBroker, "1000000.5", 50_000, 3, "0", precisionLoss),
            (usdBrokerId, walletIssuer, "1234.567", 100_000, 7, "0.01", null),
            (usdBrokerId, walletIssuer, "987654.321", 12_345, 12, "0.5", null),
            (usdBrokerId, walletIssuer, "500", 0, 4, "0", null),
            (usdBrokerId, walletIssuer, "100", 55_555, 3, "0.000001", null),
            // 19 significant digits are more than an IOU amount holds.
            (usdBrokerId, walletIssuer, "0.1234567890123456789", 10_000, 3, "0", precisionLoss),
            // A service fee finer than the scale the loan's total value sets.
            (usdBrokerId, walletIssuer, "1000000", 10_000, 3, "0.0000000001", precisionLoss),
        };

        foreach ((string brokerId, XrplWallet owner, string principal, uint rate, uint payments, string serviceFee, string refusal) in cases)
        {
            string at = $"{principal} at {rate} over {payments}";
            LOLoanBroker broker = (LOLoanBroker)(await client.LedgerEntry(new LedgerEntryRequest { Index = brokerId }).Typed()).Node;
            LOVault vault = (LOVault)(await client.LedgerEntry(new LedgerEntryRequest { Index = broker.VaultID }).Typed()).Node;

            LoanSet Terms() => new LoanSet
            {
                Account = owner.ClassicAddress,
                LoanBrokerID = brokerId,
                Counterparty = walletBorrower.ClassicAddress,
                PrincipalRequested = XrplNumber.Parse(principal),
                InterestRate = rate,
                PaymentTotal = payments,
                PaymentInterval = 60,
                GracePeriod = 60,
                LoanServiceFee = XrplNumber.Parse(serviceFee),
            };

            LoanOriginationTerms computed = LoanOrigination.Compute(Terms(), vault, broker, options: options);
            TransactionPreview<LoanSetOutcome> preview = await client.PreviewLoanSet(Terms());

            Assert.AreEqual(refusal ?? "tesSUCCESS", preview.EngineResult, $"{at}: the node's answer");
            if (!preview.WouldSucceed)
            {
                Assert.AreEqual(preview.EngineResult, computed.Refusal, $"{at}: {computed.RefusalReason}");
                continue;
            }

            Assert.IsTrue(computed.IsAccepted, $"{at}: {computed.RefusalReason}");
            LOLoan expected = preview.Outcome.Loan;
            LOLoan actual = computed.Loan;
            Assert.AreEqual(expected.PeriodicPayment, actual.PeriodicPayment, at);
            Assert.AreEqual(expected.TotalValueOutstanding, actual.TotalValueOutstanding, at);
            Assert.AreEqual(expected.PrincipalOutstanding, actual.PrincipalOutstanding, at);
            Assert.AreEqual(expected.ManagementFeeOutstanding ?? XrplNumber.Zero, actual.ManagementFeeOutstanding, at);
            Assert.AreEqual(expected.LoanScale ?? 0, actual.LoanScale, at);
            Assert.AreEqual(expected.PaymentRemaining, actual.PaymentRemaining, at);

            // The offline loan projects the same schedule as the node's.
            IReadOnlyList<LoanScheduleRow> fromNode = LoanSchedule.Project(expected, vault.Asset, broker.ManagementFeeRate ?? 0, options);
            IReadOnlyList<LoanScheduleRow> offline = LoanSchedule.Project(actual, vault.Asset, broker.ManagementFeeRate ?? 0, options);
            CollectionAssert.AreEqual(fromNode.Select(r => r.Total).ToArray(), offline.Select(r => r.Total).ToArray(), at);
        }
    }

    private static async Task<LOLoan> ReadLoan(string loanId) =>
        (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;

    /// <summary>
    /// Submits a LoanPay and returns what it did with the close time of its ledger's parent, the
    /// time rippled computes accrued and late interest at.
    /// </summary>
    private static async Task<TimedPayment> PayWithFlag(
        LoanSetOutcome loanSet,
        XrplWallet borrower,
        LoanPayFlags? flag,
        string value,
        Func<string, Currency> amount)
    {
        LoanPay pay = await client.Autofill(new LoanPay
        {
            Account = borrower.ClassicAddress,
            LoanID = loanSet.LoanId,
            Amount = amount(value),
            Flags = flag,
        });
        TransactionSummary result = await client.SubmitAndWait(pay, borrower, false);
        ValidateResult(result);

        LOLedger ledger = await client.Ledger(new LedgerRequest { LedgerHash = result.LedgerHash }).Typed();
        uint parentCloseTime = ((LedgerEntity)ledger.LedgerEntity).ParentCloseTime;
        return new TimedPayment(
            LoanPaymentOutcome.FromMetadata(pay, result.Meta),
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(parentCloseTime));
    }

    private static void AssertSame(LoanPaymentDue expected, LoanPaymentOutcome actual)
    {
        Assert.AreEqual(expected.Principal, actual.PrincipalPaid, "principal");
        Assert.AreEqual(expected.InterestToVault, actual.InterestToVault, "interest to the vault");
        Assert.AreEqual(expected.PaidToBroker, actual.PaidToBroker, "paid to the broker");
        Assert.AreEqual(expected.Total, actual.TotalPaid, "total");
    }

    private static void AssertSame(LoanAmountPayment expected, LoanPaymentOutcome actual)
    {
        Assert.IsTrue(expected.IsAccepted, expected.RefusalReason);
        AssertSame(expected.Due, actual);
        Assert.AreEqual(expected.PaymentsMade, actual.PaymentsMade, "payments made");

        LOLoan after = actual.LoanAfter;
        Assert.AreEqual(after.PrincipalOutstanding ?? XrplNumber.Zero, expected.LoanAfter.PrincipalOutstanding ?? XrplNumber.Zero, "principal outstanding");
        Assert.AreEqual(after.TotalValueOutstanding ?? XrplNumber.Zero, expected.LoanAfter.TotalValueOutstanding ?? XrplNumber.Zero, "total value outstanding");
        Assert.AreEqual(after.ManagementFeeOutstanding ?? XrplNumber.Zero, expected.LoanAfter.ManagementFeeOutstanding ?? XrplNumber.Zero, "management fee outstanding");
        Assert.AreEqual(after.PeriodicPayment, expected.LoanAfter.PeriodicPayment, "periodic payment");
        Assert.AreEqual(after.PaymentRemaining ?? 0u, expected.LoanAfter.PaymentRemaining ?? 0u, "payments remaining");
        Assert.AreEqual(after.NextPaymentDueDate, expected.LoanAfter.NextPaymentDueDate, "next due date");
    }

    /// <summary>The node's answer to a LoanPay, previewed, against the refusal computed offline.</summary>
    private static async Task AssertRefused(
        string loanId,
        LOLoan loan,
        IssuedCurrency asset,
        ushort managementFeeRate,
        XrplWallet borrower,
        string drops,
        LoanPayFlags? flag,
        LedgerRules rules)
    {
        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(new LoanPay
        {
            Account = borrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = drops, CurrencyCode = "XRP" },
            Flags = flag,
        });
        LoanAmountPayment computed = LoanPayments.PaymentForAmount(
            loan, asset, managementFeeRate, XrplNumber.Parse(drops), DateTime.UtcNow, flag == LoanPayFlags.tfLoanOverpayment, rules);

        Assert.IsFalse(preview.WouldSucceed, $"{drops}: the node takes it");
        Assert.AreEqual(preview.EngineResult, computed.Refusal, computed.RefusalReason);
    }

    private sealed record TimedPayment(LoanPaymentOutcome Outcome, DateTime ParentCloseTime);

    /// <summary>
    /// An issuer's USD vault, funded by a holder, under a broker with a management fee; the
    /// borrower holds a trust line and enough USD to pay interest and fees.
    /// </summary>
    private static async Task<string> CreateIouBroker(
        XrplWallet issuer,
        XrplWallet holder,
        XrplWallet borrower,
        IssuedCurrency usd,
        ushort managementFeeRate)
    {
        AccountSet rippling = await client.Autofill(new AccountSet
        {
            Account = issuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple,
        });
        ValidateResult(await client.SubmitAndWait(rippling, issuer, true));

        foreach (XrplWallet wallet in new[] { holder, borrower })
        {
            TrustSet trust = await client.Autofill(new TrustSet
            {
                Account = wallet.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = "100000000" },
            });
            ValidateResult(await client.SubmitAndWait(trust, wallet, true));

            Payment funding = await client.Autofill(new Payment
            {
                Account = issuer.ClassicAddress,
                Destination = wallet.ClassicAddress,
                Amount = new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = "5000000" },
            });
            ValidateResult(await client.SubmitAndWait(funding, issuer, true));
        }

        VaultCreate vaultTx = await client.Autofill(await BuildBrokerVaultAsync(client, issuer.ClassicAddress, usd));
        TransactionSummary vaultResult = await client.SubmitAndWait(vaultTx, issuer, true);
        ValidateResult(vaultResult);
        string vaultId = GetCreatedObjectId(vaultResult, LedgerEntryType.Vault);

        VaultDeposit deposit = await client.Autofill(new VaultDeposit
        {
            Account = holder.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = "2000000" },
        });
        ValidateResult(await client.SubmitAndWait(deposit, holder, true));

        LoanBrokerSet brokerTx = await client.Autofill(new LoanBrokerSet
        {
            Account = issuer.ClassicAddress,
            VaultID = vaultId,
            ManagementFeeRate = managementFeeRate,
        });
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, issuer, true);
        ValidateResult(brokerResult);
        string brokerId = GetCreatedObjectId(brokerResult, LedgerEntryType.LoanBroker);

        LoanBrokerCoverDeposit cover = await client.Autofill(new LoanBrokerCoverDeposit
        {
            Account = issuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Amount = new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = "500000" },
        });
        ValidateResult(await client.SubmitAndWait(cover, issuer, true));

        await EnterInvestmentPhaseAsync(client, vaultId);
        return brokerId;
    }

    /// <summary>Pays every projected row with the regular-payment cap and compares what the node took.</summary>
    private static async Task PayThroughAndCompare(
        IReadOnlyList<LoanScheduleRow> schedule,
        LoanSetOutcome loanSet,
        XrplWallet borrower,
        Func<string, Currency> amount)
    {
        foreach (LoanScheduleRow row in schedule)
        {
            LOLoan loan = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanSet.LoanId }).Typed()).Node;
            decimal cap = LoanPayments.RegularPaymentCap(loan, loanSet.Asset).Value;

            LoanPay pay = await client.Autofill(new LoanPay
            {
                Account = borrower.ClassicAddress,
                LoanID = loanSet.LoanId,
                Amount = amount(cap.ToString(CultureInfo.InvariantCulture)),
            });
            TransactionSummary paid = await client.SubmitAndWait(pay, borrower, false);
            ValidateResult(paid);
            LoanPaymentOutcome actual = LoanPaymentOutcome.FromMetadata(pay, paid.Meta);

            string at = "payment " + row.Number;
            Assert.AreEqual(1u, actual.PaymentsMade, at);
            Assert.AreEqual(row.Principal, actual.PrincipalPaid, at);
            Assert.AreEqual(row.Interest, actual.InterestToVault, at);
            Assert.AreEqual(row.ManagementFee + row.ServiceFee, actual.PaidToBroker, at);
            Assert.AreEqual(row.Total, actual.TotalPaid, at);
            Assert.AreEqual(row.IsFinal, actual.IsPaidOff, at);
        }
    }

    /// <summary>CreateBroker, with a management fee on the LoanBrokerSet.</summary>
    private static async Task<string> CreateBrokerWithManagementFee(XrplWallet wallet, ushort managementFeeRate)
    {
        await IntegrationTestConfig.EnsureBalanceAsync(client, wallet, 200m);
        string vaultId = await CreateVaultForBroker(client, wallet);

        VaultDeposit deposit = await client.Autofill(new VaultDeposit
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "100000000", CurrencyCode = "XRP" },
        });
        ValidateResult(await client.SubmitAndWait(deposit, wallet, true));

        LoanBrokerSet brokerTx = await client.Autofill(new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            ManagementFeeRate = managementFeeRate,
        });
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, wallet, true);
        ValidateResult(brokerResult);
        string brokerId = GetCreatedObjectId(brokerResult, LedgerEntryType.LoanBroker);

        LoanBrokerCoverDeposit cover = await client.Autofill(new LoanBrokerCoverDeposit
        {
            Account = wallet.ClassicAddress,
            LoanBrokerID = brokerId,
            Amount = new Currency { Value = "50000000", CurrencyCode = "XRP" },
        });
        ValidateResult(await client.SubmitAndWait(cover, wallet, true));

        await EnterInvestmentPhaseAsync(client, vaultId);
        return brokerId;
    }

    [TestMethod]
    public async Task TestPreviewLoanPay_MatchesTheSubmittedPayment()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);
        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 10_000_000,
            InterestRate = 10_000,
            PaymentTotal = 3,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 100,
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(loanResult);
        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);

        LOLoan loan = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;
        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
        decimal due = LoanPayments.RegularPaymentCap(loan, xrp).Value;

        LoanPay pay = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = due.ToString(System.Globalization.CultureInfo.InvariantCulture), CurrencyCode = "XRP" },
        };

        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(pay);
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);
        LoanPaymentOutcome expected = preview.Outcome;

        TransactionSummary submitted = await client.SubmitAndWait(preview.Transaction, walletBorrower, false);
        ValidateResult(submitted);
        LoanPaymentOutcome actual = LoanPaymentOutcome.FromMetadata((ILoanPay)preview.Transaction, submitted.Meta);

        Assert.AreEqual(expected.PaymentsMade, actual.PaymentsMade);
        Assert.AreEqual(expected.PrincipalPaid, actual.PrincipalPaid);
        Assert.AreEqual(expected.PaidToVault, actual.PaidToVault);
        Assert.AreEqual(expected.TotalPaid, actual.TotalPaid);
        Assert.AreEqual(expected.LoanAfter.PrincipalOutstanding, actual.LoanAfter.PrincipalOutstanding);
        Assert.AreEqual(expected.LoanAfter.TotalValueOutstanding, actual.LoanAfter.TotalValueOutstanding);

        // One regular payment; here the schedule charged the whole cap.
        Assert.AreEqual(1u, actual.PaymentsMade);
        Assert.AreEqual(due, actual.TotalPaid);
        Assert.AreEqual(100m, actual.PaidToBroker);
    }

    [TestMethod]
    public async Task TestPreviewLoanPay_Mpt_MatchesTheSubmittedPayment()
    {
        // The borrower's MPToken is authorized empty and then filled by the LoanSet, so its amount
        // appears in metadata with no previous value - the case the MPT balance reading has to
        // resolve against the issuance's OutstandingAmount. The broker's fee goes to its owner,
        // who is the MPT issuer: it leaves circulation.
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder, walletBorrower);

        (string brokerId, string mptIssuanceId) = await CreateMptBroker(client, walletIssuer, walletHolder);

        MPTokenAuthorize authorize = await client.Autofill(new MPTokenAuthorize
        {
            Account = walletBorrower.ClassicAddress,
            MPTokenIssuanceID = mptIssuanceId,
        });
        ValidateResult(await client.SubmitAndWait(authorize, walletBorrower, true));

        LoanSet loanTx = new LoanSet
        {
            Account = walletIssuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 90,
            InterestRate = 10_000,
            PaymentTotal = 3,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 1,
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletIssuer, walletBorrower);
        ValidateResult(loanResult);
        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);

        LOLoan loan = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;
        IssuedCurrency asset = new IssuedCurrency { MptIssuanceId = mptIssuanceId };
        decimal due = LoanPayments.RegularPaymentCap(loan, asset).Value;

        LoanPay pay = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = due.ToString(System.Globalization.CultureInfo.InvariantCulture), MPTokenIssuanceID = mptIssuanceId },
        };

        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(pay);
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);

        TransactionSummary submitted = await client.SubmitAndWait(preview.Transaction, walletBorrower, false);
        ValidateResult(submitted);
        LoanPaymentOutcome actual = LoanPaymentOutcome.FromMetadata((ILoanPay)preview.Transaction, submitted.Meta);

        Assert.AreEqual(preview.Outcome.PrincipalPaid, actual.PrincipalPaid);
        Assert.AreEqual(preview.Outcome.PaidToVault, actual.PaidToVault);
        Assert.AreEqual(preview.Outcome.TotalPaid, actual.TotalPaid);
        Assert.AreEqual(1u, actual.PaymentsMade);
        Assert.AreEqual(1m, actual.PaidToBroker, "the LoanServiceFee");

        // PeriodicPayment 30.00002 rounds up to 31, but the schedule charges this period 30: the
        // cap covered the payment and the node took one unit less.
        Assert.AreEqual(31m, actual.TotalPaid);
        Assert.AreEqual(32m, due);
        Assert.AreEqual(mptIssuanceId, actual.Asset.MptIssuanceId);
    }

    [TestMethod]
    public async Task TestPreviewVaultDepositAndWithdraw_MatchTheSubmittedTransactions()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet depositor = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, depositor);

        VaultCreate create = await client.Autofill(new VaultCreate
        {
            Account = owner.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        });
        TransactionSummary created = await client.SubmitAndWait(create, owner, true);
        ValidateResult(created);
        string vaultId = GetCreatedObjectId(created, LedgerEntryType.Vault);

        VaultDeposit deposit = new VaultDeposit
        {
            Account = depositor.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "10000000", CurrencyCode = "XRP" },
        };
        TransactionPreview<VaultOutcome> depositPreview = await client.PreviewVaultDeposit(deposit);
        Assert.IsTrue(depositPreview.WouldSucceed, depositPreview.EngineResult);
        TransactionSummary deposited = await client.SubmitAndWait(depositPreview.Transaction, depositor, false);
        ValidateResult(deposited);
        AssertSame(depositPreview.Outcome, VaultOutcome.FromMetadata((ITransactionCommon)depositPreview.Transaction, deposited.Meta));
        Assert.AreEqual(-10_000_000m, depositPreview.Outcome.AccountAssetChange);
        Assert.IsTrue(depositPreview.Outcome.AccountShareChange > 0);

        VaultWithdraw withdraw = new VaultWithdraw
        {
            Account = depositor.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "3000000", CurrencyCode = "XRP" },
        };
        TransactionPreview<VaultOutcome> withdrawPreview = await client.PreviewVaultWithdraw(withdraw);
        Assert.IsTrue(withdrawPreview.WouldSucceed, withdrawPreview.EngineResult);
        TransactionSummary withdrawn = await client.SubmitAndWait(withdrawPreview.Transaction, depositor, false);
        ValidateResult(withdrawn);
        AssertSame(withdrawPreview.Outcome, VaultOutcome.FromMetadata((ITransactionCommon)withdrawPreview.Transaction, withdrawn.Meta));
        Assert.AreEqual(3_000_000m, withdrawPreview.Outcome.AccountAssetChange);
        Assert.IsTrue(withdrawPreview.Outcome.AccountShareChange < 0);
    }

    [TestMethod]
    public async Task TestPreviewLoanPay_Failure_CarriesTheResultAndNoOutcome()
    {
        XrplWallet stranger = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, stranger, nodeType);

        LoanPay pay = new LoanPay
        {
            Account = stranger.ClassicAddress,
            LoanID = new string('0', 63) + "1",
            Amount = new Currency { Value = "1000", CurrencyCode = "XRP" },
        };

        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(pay);

        Assert.IsFalse(preview.WouldSucceed);
        Assert.IsFalse(string.IsNullOrEmpty(preview.EngineResult));
        Assert.IsNull(preview.Outcome);
    }

    private static void AssertSame(VaultOutcome expected, VaultOutcome actual)
    {
        Assert.AreEqual(expected.VaultId, actual.VaultId);
        Assert.AreEqual(expected.AccountAssetChange, actual.AccountAssetChange);
        Assert.AreEqual(expected.AccountShareChange, actual.AccountShareChange);
        Assert.AreEqual(expected.VaultAssetChange, actual.VaultAssetChange);
        Assert.AreEqual(expected.VaultAfter.AssetsTotal, actual.VaultAfter.AssetsTotal);
    }
}

/// <summary>
/// AMM previews against the submitted transactions; see <see cref="TestIPreviewLoanVault"/>.
/// </summary>
[TestClass]
public class TestIPreviewAmm : TestIAMMBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestPreviewAMMDepositAndWithdraw_MatchTheSubmittedTransactions()
    {
        AssertSuccess(await CreatePool("1000", 10m), "AMMCreate");

        // Single-sided: the trading fee applies, so the LP amount is not a simple proportion.
        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency { ValueAsXrp = 1m },
            Flags = AMMDepositFlags.tfSingleAsset,
        };
        TransactionPreview<AmmOutcome> depositPreview = await client.PreviewAMMDeposit(deposit);
        Assert.IsTrue(depositPreview.WouldSucceed, depositPreview.EngineResult);
        TransactionSummary deposited = await client.SubmitAndWait(depositPreview.Transaction, walletHolder, false);
        AssertSuccess(deposited, "AMMDeposit");
        AmmOutcome depositActual = AmmOutcome.FromMetadata((IAMMDeposit)depositPreview.Transaction, deposited.Meta);
        AssertSame(depositPreview.Outcome, depositActual);
        Assert.AreEqual(-1_000_000m, depositActual.Asset2Change, "1 XRP left the account");
        Assert.AreEqual(0m, depositActual.AssetChange);
        Assert.IsTrue(depositActual.LpTokenChange > 0);

        AMMInfoResponse info = await GetAmmInfo();
        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = depositActual.LpTokenChange,
            },
            Flags = AMMWithdrawFlags.tfLPToken,
        };
        TransactionPreview<AmmOutcome> withdrawPreview = await client.PreviewAMMWithdraw(withdraw);
        Assert.IsTrue(withdrawPreview.WouldSucceed, withdrawPreview.EngineResult);
        TransactionSummary withdrawn = await client.SubmitAndWait(withdrawPreview.Transaction, walletHolder, false);
        AssertSuccess(withdrawn, "AMMWithdraw");
        AmmOutcome withdrawActual = AmmOutcome.FromMetadata((IAMMWithdraw)withdrawPreview.Transaction, withdrawn.Meta);
        AssertSame(withdrawPreview.Outcome, withdrawActual);
        Assert.AreEqual(-depositActual.LpTokenChange, withdrawActual.LpTokenChange);
        Assert.IsTrue(withdrawActual.AssetChange > 0 && withdrawActual.Asset2Change > 0);
    }

    private static void AssertSame(AmmOutcome expected, AmmOutcome actual)
    {
        Assert.AreEqual(expected.AmmAccount, actual.AmmAccount);
        Assert.AreEqual(expected.AssetChange, actual.AssetChange);
        Assert.AreEqual(expected.Asset2Change, actual.Asset2Change);
        Assert.AreEqual(expected.LpTokenChange, actual.LpTokenChange);
        Assert.AreEqual(expected.AmmAfter?.LPTokenBalance?.Value, actual.AmmAfter?.LPTokenBalance?.Value);
    }
}
