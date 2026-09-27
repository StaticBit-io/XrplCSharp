using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using XrplTests.Utils;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.SugarLib;

/// <summary>
/// <see cref="LoanSchedule"/> against payments a node actually split. The integration test
/// <c>TestIPreviewLoanVault.TestLoanSchedule_MatchesEveryPaymentTheNodeTakes</c> pays whole
/// schedules; these pin the recorded cases.
/// </summary>
[TestClass]
public class TestULoanSchedule
{
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Mpt = new IssuedCurrency { MptIssuanceId = "0000014647D4494B526F324877D6EE28DCB18A81BAE0C7B0" };

    [TestMethod]
    public void TestUXrpLoan_FirstPaymentMatchesTheNode()
    {
        // The loan a recorded LoanSet preview creates; the node split the first payment of a loan
        // on these terms as 3333332 principal and 4 interest (loan-pay-xrp.json).
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("loan-set-xrp.json");
        LOLoan loan = LoanSetOutcome.FromMetadata(new LoanSet { Account = "r3RBNRimGa4M4kUBM1boCrhRmkwQg96m6f" }, simulated.Meta).Loan;

        IReadOnlyList<LoanScheduleRow> rows = LoanSchedule.Project(loan, Xrp, managementFeeRate: 0);

        Assert.HasCount(3, rows);
        Assert.AreEqual(3_333_332m, rows[0].Principal);
        Assert.AreEqual(4m, rows[0].Interest);
        Assert.AreEqual(0m, rows[0].ManagementFee);
        Assert.AreEqual(100m, rows[0].ServiceFee);
        Assert.AreEqual(3_333_436m, rows[0].Total);
        Assert.AreEqual(6_666_668m, rows[0].PrincipalOutstandingAfter);
        Assert.AreEqual(6_666_672m, rows[0].TotalValueOutstandingAfter);

        Assert.IsTrue(rows[2].IsFinal);
        Assert.AreEqual(10_000_000m, rows.Sum(r => r.Principal));
        Assert.AreEqual(8m, rows.Sum(r => r.Interest));
        Assert.AreEqual(loan.NextPaymentDueDate, rows[0].DueDate);
        Assert.AreEqual(loan.NextPaymentDueDate.Value.AddSeconds(240), rows[2].DueDate);
    }

    [TestMethod]
    public void TestUMptLoan_FirstPaymentIsBelowTheRoundedPeriodicPayment()
    {
        // A 90 MPT loan the node charged 29 principal + 1 interest for its first period, not the
        // 31 that PeriodicPayment rounded up would suggest.
        LOLoan loan = new LOLoan
        {
            PrincipalOutstanding = 90,
            TotalValueOutstanding = 91,
            PeriodicPayment = XrplNumber.Parse("30.00002283106080627"),
            LoanServiceFee = 1,
            InterestRate = 10_000,
            PaymentInterval = 120,
            PaymentRemaining = 3,
        };

        IReadOnlyList<LoanScheduleRow> rows = LoanSchedule.Project(loan, Mpt, managementFeeRate: 0);

        Assert.AreEqual(29m, rows[0].Principal);
        Assert.AreEqual(1m, rows[0].Interest);
        Assert.AreEqual(31m, rows[0].Total);
        Assert.AreEqual(90m, rows.Sum(r => r.Principal));
        Assert.AreEqual(91m, rows.Sum(r => r.Principal + r.Interest));
        Assert.IsTrue(rows.Last().IsFinal);
    }

    [TestMethod]
    public void TestUInterestFreeLoan_SplitsThePrincipalEvenly()
    {
        LOLoan loan = new LOLoan
        {
            PrincipalOutstanding = 90,
            TotalValueOutstanding = 90,
            PeriodicPayment = 30,
            PaymentInterval = 60,
            PaymentRemaining = 3,
        };

        IReadOnlyList<LoanScheduleRow> rows = LoanSchedule.Project(loan, Mpt, managementFeeRate: 0);

        CollectionAssert.AreEqual(new[] { 30m, 30m, 30m }, rows.Select(r => r.Principal).ToArray());
        Assert.IsTrue(rows.All(r => r.Interest == 0));
    }

    [TestMethod]
    public void TestUPaidOffLoan_HasNoRows()
    {
        Assert.HasCount(0, LoanSchedule.Project(new LOLoan { PaymentRemaining = 0 }, Xrp, 0));
        Assert.ThrowsExactly<ArgumentException>(() => LoanSchedule.Project(new LOLoan { PaymentRemaining = 2 }, Xrp, 0));
    }
}
