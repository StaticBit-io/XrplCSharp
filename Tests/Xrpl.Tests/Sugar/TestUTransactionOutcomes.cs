using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using XrplTests.Utils;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.SugarLib;

/// <summary>
/// Outcomes read from <c>simulate</c> responses recorded on a standalone node (<c>Fixtures/Simulate</c>).
/// </summary>
[TestClass]
public class TestUTransactionOutcomes
{
    private const string Borrower = "rUFxPcR7uA1QAd8XXLJhgQwM8qLHyReYJX";
    private const string LoanId = "104CD80EE6AB4E015296466F2CECB010247125A6401F5A7D1E2F858BF8FFECCA";
    private const string Depositor = "rQsTnntg36bd8epF47qzhgAyQzjDLDd6Dc";
    private const string VaultId = "714E461EA96F7883CCF774A1D88A0B1793398968A79277A1973CA49B78B3EE3A";

    private static LoanPay LoanPayment() => new LoanPay
    {
        Account = Borrower,
        LoanID = LoanId,
        Fee = new Currency { Value = "12" },
    };

    [TestMethod]
    public void TestULoanPay_SplitsThePaymentByWhereTheMoneyWent()
    {
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("loan-pay-xrp.json");

        LoanPaymentOutcome outcome = LoanPaymentOutcome.FromMetadata(LoanPayment(), simulated.Meta);

        Assert.AreEqual(LoanId, outcome.LoanId);
        Assert.IsTrue(outcome.Asset.IsXrp());
        Assert.AreEqual(1u, outcome.PaymentsMade);
        Assert.AreEqual(3_333_332m, outcome.PrincipalPaid);
        Assert.AreEqual(3_333_336m, outcome.PaidToVault);
        Assert.AreEqual(4m, outcome.InterestToVault);
        Assert.AreEqual(3_333_436m, outcome.TotalPaid);
        Assert.AreEqual(100m, outcome.PaidToBroker, "the LoanServiceFee");
        Assert.AreEqual(2u, outcome.LoanAfter.PaymentRemaining);
        Assert.AreEqual(XrplNumber.Parse("6666668"), outcome.LoanAfter.PrincipalOutstanding);
        Assert.IsFalse(outcome.IsPaidOff);
    }

    [TestMethod]
    public void TestULoanPay_MetadataOfAnotherLoan_Throws()
    {
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("loan-pay-xrp.json");
        LoanPay other = LoanPayment();
        other.LoanID = new string('0', 64);

        Assert.ThrowsExactly<ArgumentException>(() => LoanPaymentOutcome.FromMetadata(other, simulated.Meta));
    }

    [TestMethod]
    public void TestUVaultDeposit_MintsShares()
    {
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("vault-deposit-xrp.json");
        VaultDeposit deposit = new VaultDeposit { Account = Depositor, VaultID = VaultId, Fee = new Currency { Value = "12" } };

        VaultOutcome outcome = VaultOutcome.FromMetadata(deposit, simulated.Meta);

        Assert.AreEqual(VaultId, outcome.VaultId);
        Assert.AreEqual("000000019235C8EA92D9BC14E403C767CFDDC1A7C56AFDDD", outcome.ShareMptId);
        Assert.AreEqual(-10_000_000m, outcome.AccountAssetChange);
        Assert.AreEqual(10_000_000m, outcome.AccountShareChange);
        Assert.AreEqual(10_000_000m, outcome.VaultAssetChange);
        Assert.AreEqual(XrplNumber.Parse("10000000"), outcome.VaultAfter.AssetsTotal);
    }

    [TestMethod]
    public void TestUVaultWithdraw_BurnsShares()
    {
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("vault-withdraw-xrp.json");
        VaultWithdraw withdraw = new VaultWithdraw { Account = Depositor, VaultID = VaultId, Fee = new Currency { Value = "12" } };

        VaultOutcome outcome = VaultOutcome.FromMetadata(withdraw, simulated.Meta);

        Assert.AreEqual(3_000_000m, outcome.AccountAssetChange);
        Assert.AreEqual(-3_000_000m, outcome.AccountShareChange);
        Assert.AreEqual(-3_000_000m, outcome.VaultAssetChange);
    }

    [TestMethod]
    public void TestURegularPaymentCap_MatchesWhatTheNodeTook()
    {
        // The recorded payment of 3333436 drops was exactly one regular payment.
        LOLoan loan = new LOLoan
        {
            PeriodicPayment = XrplNumber.Parse("3333335.870117867363"),
            LoanServiceFee = 100,
            PaymentRemaining = 3,
        };

        Assert.AreEqual(3_333_436m, LoanPayments.RegularPaymentCap(loan, new IssuedCurrency { Currency = "XRP" }));
    }

    [TestMethod]
    public void TestURegularPaymentCap_IssuedCurrencyRoundsUpToLoanScale()
    {
        LOLoan loan = new LOLoan
        {
            PeriodicPayment = XrplNumber.Parse("1.001"),
            LoanServiceFee = (XrplNumber)0.09m,
            LoanScale = -2,
        };

        Assert.AreEqual(1.10m, LoanPayments.RegularPaymentCap(loan, new IssuedCurrency { Currency = "USD", Issuer = Depositor }));
    }

    [TestMethod]
    public void TestURegularPaymentCap_MptRoundsUpToWholeUnits()
    {
        LOLoan loan = new LOLoan { PeriodicPayment = XrplNumber.Parse("7.2") };

        Assert.AreEqual(8m, LoanPayments.RegularPaymentCap(loan, new IssuedCurrency { MptIssuanceId = "0000000100000000000000000000000000000000000000AA" }));
        Assert.IsNull(LoanPayments.RegularPaymentCap(new LOLoan(), new IssuedCurrency { Currency = "XRP" }));
    }

    [TestMethod]
    public void TestURegularPaymentCap_FinalPaymentIsTheWholeRemainder()
    {
        // rippled's final payment clears TotalValueOutstanding whatever PeriodicPayment says.
        LOLoan loan = new LOLoan
        {
            PeriodicPayment = XrplNumber.Parse("30.00002283106080627"),
            TotalValueOutstanding = 31,
            LoanServiceFee = 1,
            PaymentRemaining = 1,
        };

        Assert.AreEqual(32m, LoanPayments.RegularPaymentCap(loan, new IssuedCurrency { MptIssuanceId = "0000000100000000000000000000000000000000000000AA" }));
    }

    [TestMethod]
    public void TestUIsPaymentLate_BoundaryFollowsTheAmendment()
    {
        DateTime due = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        LOLoan loan = new LOLoan { NextPaymentDueDate = due, GracePeriod = 60 };

        Assert.IsFalse(LoanPayments.IsPaymentLate(loan, due.AddSeconds(-1), dueTimeIsLate: true));
        Assert.IsTrue(LoanPayments.IsPaymentLate(loan, due, dueTimeIsLate: true));
        Assert.IsFalse(LoanPayments.IsPaymentLate(loan, due, dueTimeIsLate: false));
        Assert.IsTrue(LoanPayments.IsPaymentLate(loan, due.AddSeconds(1), dueTimeIsLate: false));

        Assert.IsFalse(LoanPayments.IsPastGracePeriod(loan, due.AddSeconds(59), boundaryIsPast: true));
        Assert.IsTrue(LoanPayments.IsPastGracePeriod(loan, due.AddSeconds(60), boundaryIsPast: true));
        Assert.IsFalse(LoanPayments.IsPastGracePeriod(loan, due.AddSeconds(60), boundaryIsPast: false));
        Assert.IsTrue(LoanPayments.IsPastGracePeriod(loan, due.AddSeconds(61), boundaryIsPast: false));

        Assert.IsFalse(LoanPayments.IsPaymentLate(new LOLoan(), due, dueTimeIsLate: true));
    }
}
