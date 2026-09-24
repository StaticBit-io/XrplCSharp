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
    public void TestULoanSet_ReadsTheTermsBeforeTheBorrowerSigns()
    {
        // Simulated with an empty CounterpartySignature; the node applied it as it would the signed one.
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("loan-set-xrp.json");
        LoanSet loanSet = new LoanSet
        {
            Account = "r3RBNRimGa4M4kUBM1boCrhRmkwQg96m6f",
            Counterparty = "r9GxKYQKL6b7XuSy2xFzdnWdQr4BKhxswP",
            LoanBrokerID = "B38F16BD0F293DFADE3C7A27F9044827993F0E0524E05D6C36D89E08EDD84027",
            Fee = new Currency { Value = "24" },
        };

        LoanSetOutcome outcome = LoanSetOutcome.FromMetadata(loanSet, simulated.Meta);

        Assert.AreEqual("AB0139E07A3E029D30AFAB3FB6BF818395F5B557915EAA84A72977CC97740A82", outcome.LoanId);
        Assert.IsTrue(outcome.Asset.IsXrp());
        Assert.AreEqual(XrplNumber.Parse("3333335.870117867363"), outcome.Loan.PeriodicPayment);
        Assert.AreEqual(3u, outcome.Loan.PaymentRemaining);
        Assert.AreEqual(9_999_000m, outcome.BorrowerReceives, "principal less the 1000-drop origination fee");
        Assert.AreEqual(10_000_000m, outcome.PaidFromVault);
        Assert.AreEqual(8m, outcome.InterestTotal);
        Assert.AreEqual(300m, outcome.ServiceFeesTotal);
        Assert.AreEqual(10_000_308m, outcome.TotalToRepay);
    }

    [TestMethod]
    public void TestULoanSet_MetadataWithoutALoan_Throws()
    {
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("vault-deposit-xrp.json");

        Assert.ThrowsExactly<ArgumentException>(() => LoanSetOutcome.FromMetadata(new LoanSet { Account = Depositor }, simulated.Meta));
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
    public void TestUVaultDeposit_ByTheIssuerOfTheAsset_CountsItsOwnCurrency()
    {
        // An issuer depositing its own USD: its side of the trust line names the vault as the
        // counterparty, not itself.
        const string issuer = "rUFxPcR7uA1QAd8XXLJhgQwM8qLHyReYJX";
        const string vault = "rNLaas7dZix3WviwSq41WDQYxCtvnqyFGQ";
        const string shares = "000000019235C8EA92D9BC14E403C767CFDDC1A7C56AFDDD";
        string json = $@"{{
            ""TransactionIndex"": 0,
            ""TransactionResult"": ""tesSUCCESS"",
            ""AffectedNodes"": [
                {{ ""ModifiedNode"": {{
                    ""LedgerEntryType"": ""Vault"",
                    ""LedgerIndex"": ""{VaultId}"",
                    ""FinalFields"": {{ ""Account"": ""{vault}"", ""Asset"": {{ ""currency"": ""USD"", ""issuer"": ""{issuer}"" }}, ""AssetsAvailable"": ""100"", ""AssetsTotal"": ""100"", ""Flags"": 0, ""Owner"": ""{issuer}"", ""OwnerNode"": ""0"", ""Sequence"": 5, ""ShareMPTID"": ""{shares}"", ""WithdrawalPolicy"": 1 }}
                }} }},
                {{ ""ModifiedNode"": {{
                    ""LedgerEntryType"": ""RippleState"",
                    ""LedgerIndex"": ""0000000000000000000000000000000000000000000000000000000000000003"",
                    ""PreviousFields"": {{ ""Balance"": {{ ""currency"": ""USD"", ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"", ""value"": ""0"" }} }},
                    ""FinalFields"": {{
                        ""Balance"": {{ ""currency"": ""USD"", ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"", ""value"": ""100"" }},
                        ""Flags"": 0,
                        ""LowLimit"": {{ ""currency"": ""USD"", ""issuer"": ""{vault}"", ""value"": ""0"" }},
                        ""HighLimit"": {{ ""currency"": ""USD"", ""issuer"": ""{issuer}"", ""value"": ""0"" }}
                    }}
                }} }},
                {{ ""CreatedNode"": {{
                    ""LedgerEntryType"": ""MPToken"",
                    ""LedgerIndex"": ""0000000000000000000000000000000000000000000000000000000000000004"",
                    ""NewFields"": {{ ""Account"": ""{issuer}"", ""MPTAmount"": ""100"", ""MPTokenIssuanceID"": ""{shares}"" }}
                }} }},
                {{ ""ModifiedNode"": {{
                    ""LedgerEntryType"": ""MPTokenIssuance"",
                    ""LedgerIndex"": ""0000000000000000000000000000000000000000000000000000000000000005"",
                    ""PreviousFields"": {{ ""OutstandingAmount"": ""0"" }},
                    ""FinalFields"": {{ ""Flags"": 56, ""Issuer"": ""{vault}"", ""OutstandingAmount"": ""100"", ""OwnerNode"": ""0"", ""Sequence"": 1 }}
                }} }}
            ]
        }}";
        Meta meta = System.Text.Json.JsonSerializer.Deserialize<Meta>(json, global::Xrpl.Client.Json.XrplJsonOptions.Default);
        VaultDeposit deposit = new VaultDeposit { Account = issuer, VaultID = VaultId, Fee = new Currency { Value = "12" } };

        VaultOutcome outcome = VaultOutcome.FromMetadata(deposit, meta);

        Assert.AreEqual(-100m, outcome.AccountAssetChange);
        Assert.AreEqual(100m, outcome.AccountShareChange);
        Assert.AreEqual(100m, outcome.VaultAssetChange);
    }

    [TestMethod]
    public void TestURegularPaymentCap_RoundsUpExactlyBeforeConvertingToDecimal()
    {
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = Depositor };

        // 1.000000000000001e-20 has digits past decimal's 28 places; converting first would round
        // it down to 1e-20 and the cap would fall below the payment.
        LOLoan justAbove = new LOLoan { PeriodicPayment = XrplNumber.Parse("1.000000000000001e-20"), LoanScale = -20, PaymentRemaining = 2 };
        Assert.AreEqual(0.00000000000000000002m, LoanPayments.RegularPaymentCap(justAbove, usd));

        // Below decimal's smallest step the cap rounds up to it rather than to zero.
        LOLoan tiny = new LOLoan { PeriodicPayment = XrplNumber.Parse("1e-29"), LoanScale = -30, PaymentRemaining = 2 };
        Assert.AreEqual(0.0000000000000000000000000001m, LoanPayments.RegularPaymentCap(tiny, usd));

        // A fine scale does not overflow a payment that needs no rounding.
        LOLoan fine = new LOLoan { PeriodicPayment = 10, LoanScale = -28, PaymentRemaining = 2 };
        Assert.AreEqual(10m, LoanPayments.RegularPaymentCap(fine, usd));
    }

    [TestMethod]
    public void TestURegularPaymentCap_FinalPaymentAndFeeRoundUpBeforeConverting()
    {
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = Depositor };

        // Converting 1e-29 to decimal first would give zero, which cannot settle anything.
        LOLoan final = new LOLoan { TotalValueOutstanding = XrplNumber.Parse("1e-29"), LoanScale = -30, PaymentRemaining = 1 };
        Assert.AreEqual(0.0000000000000000000000000001m, LoanPayments.RegularPaymentCap(final, usd));

        LOLoan withTinyFee = new LOLoan { PeriodicPayment = 5, LoanServiceFee = XrplNumber.Parse("1e-29"), LoanScale = -30, PaymentRemaining = 2 };
        Assert.AreEqual(5.0000000000000000000000000001m, LoanPayments.RegularPaymentCap(withTinyFee, usd));
    }

    [TestMethod]
    public void TestUVaultOutcome_OtherVaultOrOtherTransaction_Throws()
    {
        SimulateResponse simulated = TestUBalanceChangesMpt.LoadSimulate("vault-deposit-xrp.json");

        VaultDeposit otherVault = new VaultDeposit { Account = Depositor, VaultID = new string('0', 64) };
        Assert.ThrowsExactly<ArgumentException>(() => VaultOutcome.FromMetadata(otherVault, simulated.Meta));

        Assert.ThrowsExactly<ArgumentException>(() => VaultOutcome.FromMetadata(LoanPayment(), simulated.Meta));
    }

    [TestMethod]
    public void TestUIsPaymentLate_ComparesInUtc()
    {
        DateTime due = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        LOLoan loan = new LOLoan { NextPaymentDueDate = due };

        // The same instants, written as local time.
        Assert.IsFalse(LoanPayments.IsPaymentLate(loan, due.AddSeconds(-1).ToLocalTime(), dueTimeIsLate: false));
        Assert.IsTrue(LoanPayments.IsPaymentLate(loan, due.AddSeconds(1).ToLocalTime(), dueTimeIsLate: false));

        // An unspecified time is read as UTC.
        Assert.IsTrue(LoanPayments.IsPaymentLate(loan, DateTime.SpecifyKind(due.AddSeconds(1), DateTimeKind.Unspecified), dueTimeIsLate: false));
    }

    [TestMethod]
    public void TestURegularPaymentCap_BeyondDecimal_IsNull()
    {
        LOLoan loan = new LOLoan
        {
            TotalValueOutstanding = XrplNumber.Parse("7e28"),
            LoanServiceFee = XrplNumber.Parse("1e28"),
            PaymentRemaining = 1,
        };

        Assert.IsNull(LoanPayments.RegularPaymentCap(loan, new IssuedCurrency { Currency = "USD", Issuer = Depositor }));
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
