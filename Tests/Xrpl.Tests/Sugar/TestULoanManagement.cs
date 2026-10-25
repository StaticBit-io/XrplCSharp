using System;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestULoanManagement
{
    private static readonly LedgerRules Fixed = new LedgerRules();
    private static readonly LedgerRules Legacy = new LedgerRules { FixCleanup3_4_0 = false };

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };
    private static readonly IssuedCurrency Mpt = new IssuedCurrency { MptIssuanceId = "00000001A407AF5856CCF3C42619DAA925813FC955C72983" };

    private const string Owner = "rM5aC3ZApFENp53re6N2UmRCjU1AFzrY3d";
    private const string Stranger = "rQsTnntg36bd8epF47qzhgAyQzjDLDd6Dc";
    private const string BrokerId = "B7C4F2D4C6C7A1C1C9E1A6F9E7D8B5A4C3B2A1F0E9D8C7B6A5F4E3D2C1B0A9F8";
    private const string VaultId = "714E461EA96F7883CCF774A1D88A0B1793398968A79277A1973CA49B78B3EE3A";
    private const string LoanId = "104CD80EE6AB4E015296466F2CECB010247125A6401F5A7D1E2F858BF8FFECCA";

    private static readonly DateTime Due = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private const uint Grace = 600;

    private static LOLoan Loan(
        string totalValue = "10500000",
        string managementFee = "50000",
        string principal = "10000000",
        LoanFlags flags = 0,
        uint paymentRemaining = 3,
        int loanScale = 0) => new LOLoan
    {
        LoanBrokerID = BrokerId,
        Flags = flags,
        TotalValueOutstanding = XrplNumber.Parse(totalValue),
        ManagementFeeOutstanding = XrplNumber.Parse(managementFee),
        PrincipalOutstanding = XrplNumber.Parse(principal),
        PaymentRemaining = paymentRemaining,
        NextPaymentDueDate = Due,
        GracePeriod = Grace,
        PaymentInterval = 3600,
        StartDate = Due.AddSeconds(-3600),
        LoanScale = loanScale,
    };

    private static LOVault Vault(IssuedCurrency asset, string total, string available, string loss = "0", bool cashBasis = false) => new LOVault
    {
        Index = VaultId,
        Asset = asset,
        AssetsTotal = XrplNumber.Parse(total),
        AssetsAvailable = XrplNumber.Parse(available),
        LossUnrealized = XrplNumber.Parse(loss),
        LEVersion = cashBasis ? (uint)VaultVersion.CashBasis : null,
    };

    private static LOLoanBroker Broker(string debt, uint coverRateMinimum, uint coverRateLiquidation, string cover) => new LOLoanBroker
    {
        Index = BrokerId,
        Owner = Owner,
        VaultID = VaultId,
        DebtTotal = XrplNumber.Parse(debt),
        CoverRateMinimum = coverRateMinimum,
        CoverRateLiquidation = coverRateLiquidation,
        CoverAvailable = XrplNumber.Parse(cover),
    };

    private static LOVault XrpVault(string available = "40000000", bool cashBasis = false) =>
        Vault(Xrp, "100000000", available, cashBasis: cashBasis);

    [TestMethod]
    public void Exposure_FollowsTheVaultsAccountingBasis()
    {
        // Accrual: TotalValueOutstanding less ManagementFeeOutstanding. Cash basis (LEVersion 1):
        // PrincipalOutstanding.
        Assert.AreEqual(10_450_000m, LoanManagement.Exposure(Loan(), XrpVault()));
        Assert.AreEqual(10_000_000m, LoanManagement.Exposure(Loan(), XrpVault(cashBasis: true)));
    }

    [TestMethod]
    public void Availability_UnderTheFix_BothBoundariesAreExclusive()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 10_000, "5000000");

        LoanManageAvailability atDue = LoanManagement.Availability(Loan(), broker, Owner, Due, Fixed);
        Assert.AreEqual("tecTOO_SOON", atDue.Impair.Refusal);
        Assert.AreEqual(Due.AddSeconds(1), atDue.ImpairableAt);
        Assert.AreEqual(Due.AddSeconds(Grace + 1), atDue.DefaultableAt);

        Assert.IsTrue(LoanManagement.Availability(Loan(), broker, Owner, Due.AddSeconds(1), Fixed).Impair.IsAllowed);

        LoanManageAvailability atGraceEnd = LoanManagement.Availability(Loan(), broker, Owner, Due.AddSeconds(Grace), Fixed);
        Assert.IsTrue(atGraceEnd.Impair.IsAllowed);
        Assert.AreEqual("tecTOO_SOON", atGraceEnd.Default.Refusal);

        Assert.IsTrue(LoanManagement.Availability(Loan(), broker, Owner, Due.AddSeconds(Grace + 1), Fixed).Default.IsAllowed);
    }

    /// <summary>A close time is whole seconds: a fraction inside the boundary second is still the boundary.</summary>
    [TestMethod]
    public void UnderTheFix_AFractionPastAnExclusiveBoundaryIsNotPastIt()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 10_000, "5000000");

        LoanManageAvailability insideDueSecond = LoanManagement.Availability(Loan(), broker, Owner, Due.AddMilliseconds(400), Fixed);
        Assert.AreEqual("tecTOO_SOON", insideDueSecond.Impair.Refusal);

        LoanManageAvailability insideGraceSecond = LoanManagement.Availability(Loan(), broker, Owner, Due.AddSeconds(Grace).AddMilliseconds(400), Fixed);
        Assert.AreEqual("tecTOO_SOON", insideGraceSecond.Default.Refusal);

        Assert.AreEqual("tecTOO_SOON", LoanManagement.Impair(Loan(), XrpVault(), Due.AddMilliseconds(400), Fixed).Refusal);
    }

    [TestMethod]
    public void Availability_BeforeTheFix_ImpairHasNoTimeConditionAndTheDefaultBoundaryIsInclusive()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 10_000, "5000000");

        LoanManageAvailability early = LoanManagement.Availability(Loan(), broker, Owner, Due.AddDays(-10), Legacy);
        Assert.IsTrue(early.Impair.IsAllowed);
        Assert.IsNull(early.ImpairableAt);
        Assert.AreEqual(Due.AddSeconds(Grace), early.DefaultableAt);

        Assert.AreEqual("tecTOO_SOON", LoanManagement.Availability(Loan(), broker, Owner, Due.AddSeconds(Grace - 1), Legacy).Default.Refusal);
        Assert.IsTrue(LoanManagement.Availability(Loan(), broker, Owner, Due.AddSeconds(Grace), Legacy).Default.IsAllowed);
    }

    [TestMethod]
    public void Availability_StateTransitions()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 10_000, "5000000");
        DateTime late = Due.AddSeconds(Grace + 1);

        LoanManageAvailability current = LoanManagement.Availability(Loan(), broker, Owner, late, Fixed);
        Assert.IsTrue(current.Impair.IsAllowed);
        Assert.AreEqual("tecNO_PERMISSION", current.Unimpair.Refusal);
        Assert.IsTrue(current.Default.IsAllowed);

        LoanManageAvailability impaired = LoanManagement.Availability(Loan(flags: LoanFlags.lsfLoanImpaired), broker, Owner, late, Fixed);
        Assert.AreEqual("tecNO_PERMISSION", impaired.Impair.Refusal);
        Assert.IsTrue(impaired.Unimpair.IsAllowed);
        Assert.IsTrue(impaired.Default.IsAllowed);

        foreach (LOLoan frozen in new[] { Loan(flags: LoanFlags.lsfLoanDefault), Loan(paymentRemaining: 0) })
        {
            LoanManageAvailability none = LoanManagement.Availability(frozen, broker, Owner, late, Fixed);
            Assert.AreEqual("tecNO_PERMISSION", none.Impair.Refusal);
            Assert.AreEqual("tecNO_PERMISSION", none.Unimpair.Refusal);
            Assert.AreEqual("tecNO_PERMISSION", none.Default.Refusal);
        }
    }

    [TestMethod]
    public void Availability_OnlyTheBrokersOwner_InTheNodesOrderOfChecks()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 10_000, "5000000");

        LoanManageAvailability late = LoanManagement.Availability(Loan(), broker, Stranger, Due.AddSeconds(Grace + 1), Fixed);
        Assert.AreEqual("tecNO_PERMISSION", late.Impair.Refusal);
        Assert.AreEqual("tecNO_PERMISSION", late.Default.Refusal);

        // preclaim checks the default's time before the owner; the impairment's time is checked in
        // doApply, after the owner.
        LoanManageAvailability early = LoanManagement.Availability(Loan(), broker, Stranger, Due, Fixed);
        Assert.AreEqual("tecTOO_SOON", early.Default.Refusal);
        Assert.AreEqual("tecNO_PERMISSION", early.Impair.Refusal);

        Assert.IsTrue(LoanManagement.Availability(Loan(), broker, null, Due.AddSeconds(Grace + 1), Fixed).Default.IsAllowed);
    }

    [TestMethod]
    public void Availability_ABrokerOfAnotherLoanIsRejected()
    {
        LOLoanBroker other = Broker("1", 0, 0, "0");
        LOLoanBroker foreign = new LOLoanBroker { Index = VaultId, Owner = Owner };
        Assert.IsNotNull(LoanManagement.Availability(Loan(), other, Owner, Due, Fixed));
        Assert.ThrowsExactly<ArgumentException>(() => LoanManagement.Availability(Loan(), foreign, Owner, Due, Fixed));
    }

    [TestMethod]
    public void Impair_Xrp_BooksTheExposureAsUnrealizedLoss()
    {
        LoanManageQuote quote = LoanManagement.Impair(Loan(), XrpVault(), Due.AddSeconds(1), Fixed, sharesOutstanding: 100_000_000);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(LoanManageAction.Impair, quote.Action);
        Assert.AreEqual(10_450_000m, quote.Exposure);
        Assert.AreEqual(0m, quote.LossUnrealizedBefore);
        Assert.AreEqual(10_450_000m, quote.LossUnrealizedAfter);
        Assert.AreEqual(100_000_000m, quote.AssetsTotalAfter);
        Assert.AreEqual(40_000_000m, quote.AssetsAvailableAfter);
        Assert.AreEqual(0m, quote.VaultLoss);
        Assert.IsNull(quote.DebtTotalAfter);
        Assert.AreEqual(Due, quote.NextPaymentDueDateAfter);
        Assert.AreEqual(1m, quote.ShareValueBefore);
        Assert.AreEqual(0.8955m, quote.ShareValueAfter);
    }

    [TestMethod]
    public void Impair_UnderTheFix_ExactlyAtTheDueDateIsTooSoon()
    {
        LoanManageQuote quote = LoanManagement.Impair(Loan(), XrpVault(), Due, Fixed);
        Assert.AreEqual("tecTOO_SOON", quote.Refusal);
        Assert.AreEqual(0m, quote.LossUnrealizedAfter);
        Assert.AreEqual(10_450_000m, quote.Exposure);
    }

    [TestMethod]
    public void Impair_BeforeTheFix_MovesANotYetLateDueDateToTheParentCloseTime()
    {
        DateTime early = Due.AddSeconds(-100);
        LoanManageQuote moved = LoanManagement.Impair(Loan(), XrpVault(), early, Legacy);
        Assert.IsTrue(moved.IsAccepted, moved.RefusalReason);
        Assert.AreEqual(early, moved.NextPaymentDueDateAfter);

        // Exactly at the due date the payment counts as late before the fix: the date stays.
        LoanManageQuote kept = LoanManagement.Impair(Loan(), XrpVault(), Due, Legacy);
        Assert.IsTrue(kept.IsAccepted, kept.RefusalReason);
        Assert.AreEqual(Due, kept.NextPaymentDueDateAfter);
    }

    [TestMethod]
    public void Impair_LimitExceeded_DependsOnTheAccountingBasis()
    {
        // 10,000,000 drops lent out. The accrual exposure (10,450,000) exceeds it; the cash-basis
        // exposure (the principal, 10,000,000) equals it, which the node accepts.
        LoanManageQuote accrual = LoanManagement.Impair(Loan(), XrpVault(available: "90000000"), Due.AddSeconds(1), Fixed);
        Assert.AreEqual("tecLIMIT_EXCEEDED", accrual.Refusal);
        Assert.AreEqual(0m, accrual.LossUnrealizedAfter);

        LoanManageQuote cash = LoanManagement.Impair(Loan(), XrpVault(available: "90000000", cashBasis: true), Due.AddSeconds(1), Fixed);
        Assert.IsTrue(cash.IsAccepted, cash.RefusalReason);
        Assert.AreEqual(10_000_000m, cash.LossUnrealizedAfter);
    }

    [TestMethod]
    public void Impair_Iou_RoundsTheLossToTheScaleOfAssetsTotal()
    {
        // AssetsTotal 1000000.5 has scale 10^-9: the exposure 123.4567890126 books as 123.456789013.
        LOLoan loan = Loan(totalValue: "123.4567890126", managementFee: "0", principal: "120", loanScale: -10);
        LOVault vault = Vault(Usd, "1000000.5", "900000");

        LoanManageQuote impair = LoanManagement.Impair(loan, vault, Due.AddSeconds(1), Fixed);
        Assert.IsTrue(impair.IsAccepted, impair.RefusalReason);
        Assert.AreEqual(123.456789013m, impair.LossUnrealizedAfter);

        // Unimpairing takes the exposure back out and the 4e-10 left over rounds to zero.
        LOLoan impaired = Loan(totalValue: "123.4567890126", managementFee: "0", principal: "120", flags: LoanFlags.lsfLoanImpaired, loanScale: -10);
        LoanManageQuote unimpair = LoanManagement.Unimpair(impaired, Vault(Usd, "1000000.5", "900000", "123.456789013"), Due.AddSeconds(1), Fixed);
        Assert.IsTrue(unimpair.IsAccepted, unimpair.RefusalReason);
        Assert.AreEqual(0m, unimpair.LossUnrealizedAfter);
    }

    [TestMethod]
    public void Unimpair_RequiresAnImpairedLoan()
    {
        Assert.AreEqual("tecNO_PERMISSION", LoanManagement.Unimpair(Loan(), XrpVault(), Due, Fixed).Refusal);
        Assert.AreEqual("tecNO_PERMISSION", LoanManagement.Impair(Loan(flags: LoanFlags.lsfLoanImpaired), XrpVault(), Due.AddSeconds(1), Fixed).Refusal);
        Assert.AreEqual("tecNO_PERMISSION", LoanManagement.Impair(Loan(paymentRemaining: 0), XrpVault(), Due.AddSeconds(1), Fixed).Refusal);
    }

    [TestMethod]
    public void Unimpair_BeforeTheFix_RecomputesTheDueDate()
    {
        LOLoan loan = Loan(flags: LoanFlags.lsfLoanImpaired);
        LOVault vault = Vault(Xrp, "100000000", "40000000", "10450000");

        // The regular due date is StartDate + PaymentInterval = Due; it holds while it has not passed.
        LoanManageQuote within = LoanManagement.Unimpair(loan, vault, Due.AddSeconds(-1), Legacy);
        Assert.IsTrue(within.IsAccepted, within.RefusalReason);
        Assert.AreEqual(0m, within.LossUnrealizedAfter);
        Assert.AreEqual(Due, within.NextPaymentDueDateAfter);

        LoanManageQuote after = LoanManagement.Unimpair(loan, vault, Due, Legacy);
        Assert.AreEqual(Due.AddSeconds(3600), after.NextPaymentDueDateAfter);

        LoanManageQuote fixedRules = LoanManagement.Unimpair(loan, vault, Due.AddDays(2), Fixed);
        Assert.AreEqual(Due, fixedRules.NextPaymentDueDateAfter);
    }

    [TestMethod]
    public void Default_Xrp_LiquidatesTheCoverRoundedUpToAWholeDrop()
    {
        // Minimum cover 10% of 20000001 = 2000000.1; liquidated 33.333% of it = 666660.0333333,
        // rounded up to 666661 drops. The vault loses the rest of the exposure.
        LoanManageQuote quote = LoanManagement.Default(Loan(), Broker("20000001", 10_000, 33_333, "5000000"), XrpVault(), Fixed, sharesOutstanding: 100_000_000);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(LoanManageAction.Default, quote.Action);
        Assert.AreEqual(666_661m, quote.DefaultCovered);
        Assert.AreEqual(9_783_339m, quote.VaultLoss);
        Assert.AreEqual(90_216_661m, quote.AssetsTotalAfter);
        Assert.AreEqual(40_666_661m, quote.AssetsAvailableAfter);
        Assert.AreEqual(0m, quote.LossUnrealizedAfter);
        Assert.AreEqual(20_000_001m, quote.DebtTotalBefore);
        Assert.AreEqual(9_550_001m, quote.DebtTotalAfter);
        Assert.AreEqual(5_000_000m, quote.CoverAvailableBefore);
        Assert.AreEqual(4_333_339m, quote.CoverAvailableAfter);
        Assert.IsNull(quote.NextPaymentDueDateAfter);
        Assert.AreEqual(1m, quote.ShareValueBefore);
        Assert.AreEqual(0.90216661m, quote.ShareValueAfter);
    }

    [TestMethod]
    public void Default_Iou_RoundsTheCoverUpAtTheLoanScaleAndTheLossDownAtTheVaultScale()
    {
        // Minimum cover 12.345% of 1234.567891 = 152.40740614395; liquidated 33.333% of it =
        // 50.8019606899628535, rounded up at the loan scale 10^-6 to 50.801961. The exposure
        // 600.1 less that, 549.298039, rounds down at the vault scale 10^-5 to 549.29803.
        LOLoan loan = Loan(totalValue: "600.1234567", managementFee: "0.0234567", principal: "550", loanScale: -6);
        LOVault vault = Vault(Usd, "12345678901.5", "12345670000");
        LOLoanBroker broker = Broker("1234.567891", 12_345, 33_333, "200");

        LoanManageQuote quote = LoanManagement.Default(loan, broker, vault, Fixed, sharesOutstanding: 1_000_000_000);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(600.1m, quote.Exposure);
        Assert.AreEqual(50.801961m, quote.DefaultCovered);
        Assert.AreEqual(549.29803m, quote.VaultLoss);
        Assert.AreEqual(12_345_678_352.20197m, quote.AssetsTotalAfter);
        // 12345670050.801961 needs 17 digits; the stored amount keeps 16.
        Assert.AreEqual(12_345_670_050.80196m, quote.AssetsAvailableAfter);
        Assert.AreEqual(634.46789m, quote.DebtTotalAfter);
        Assert.AreEqual(149.198039m, quote.CoverAvailableAfter);
        Assert.AreEqual(12.3456789015m, quote.ShareValueBefore);
        Assert.AreEqual(12.34567835220197m, quote.ShareValueAfter);
    }

    [TestMethod]
    public void Default_ImpairedLoan_RealizesTheUnrealizedLoss()
    {
        LOLoan loan = Loan(totalValue: "600.1234567", managementFee: "0.0234567", principal: "550", flags: LoanFlags.lsfLoanImpaired, loanScale: -6);
        LOVault vault = Vault(Usd, "12345678901.5", "12345670000", "700");
        LOLoanBroker broker = Broker("1234.567891", 12_345, 33_333, "200");

        LoanManageQuote quote = LoanManagement.Default(loan, broker, vault, Fixed, sharesOutstanding: 1_000_000_000);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(700m, quote.LossUnrealizedBefore);
        Assert.AreEqual(99.9m, quote.LossUnrealizedAfter);
        Assert.AreEqual(549.29803m, quote.VaultLoss);
        Assert.AreEqual(12.3456782015m, quote.ShareValueBefore);
        Assert.AreEqual(12.34567825230197m, quote.ShareValueAfter);
    }

    [TestMethod]
    public void Default_Mpt_CoverSmallerThanTheLiquidationAmount_BothBases()
    {
        // 10% of the 300000 debt is 30000, 10% of that 3000; the broker holds only 1000.
        LOLoan loan = Loan(totalValue: "330000", managementFee: "3000", principal: "300000");
        LOLoanBroker broker = Broker("300000", 10_000, 10_000, "1000");

        LoanManageQuote cash = LoanManagement.Default(loan, broker, Vault(Mpt, "1000000", "600000", cashBasis: true), Fixed);
        Assert.IsTrue(cash.IsAccepted, cash.RefusalReason);
        Assert.AreEqual(300_000m, cash.Exposure);
        Assert.AreEqual(1_000m, cash.DefaultCovered);
        Assert.AreEqual(299_000m, cash.VaultLoss);
        Assert.AreEqual(701_000m, cash.AssetsTotalAfter);
        Assert.AreEqual(601_000m, cash.AssetsAvailableAfter);
        Assert.AreEqual(0m, cash.DebtTotalAfter);
        Assert.AreEqual(0m, cash.CoverAvailableAfter);

        LoanManageQuote accrual = LoanManagement.Default(loan, broker, Vault(Mpt, "1000000", "600000"), Fixed);
        Assert.AreEqual(327_000m, accrual.Exposure);
        Assert.AreEqual(326_000m, accrual.VaultLoss);
        Assert.AreEqual(674_000m, accrual.AssetsTotalAfter);
        Assert.AreEqual(0m, accrual.DebtTotalAfter);
    }

    [TestMethod]
    public void Default_Iou_DustAboveTheTotalRaisesTheTotalToTheAvailableAssets()
    {
        // The loss 100.0000000000004 - 4e-13 = 100 comes off AssetsTotal (900), and the cover 4e-13
        // goes on AssetsAvailable (900.0000000000004). The 4e-13 excess is 15 orders of magnitude
        // below the available assets: dust, so the node sets the total to the available assets.
        LOLoan loan = Loan(totalValue: "100.0000000000004", managementFee: "0", principal: "100", loanScale: -13);
        LOLoanBroker broker = Broker("1000", 100_000, 100_000, "0.0000000000004");

        LoanManageQuote quote = LoanManagement.Default(loan, broker, Vault(Usd, "1000", "900"), Fixed);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(0.0000000000004m, quote.DefaultCovered);
        Assert.AreEqual(100m, quote.VaultLoss);
        Assert.AreEqual(900.0000000000004m, quote.AssetsAvailableAfter);
        Assert.AreEqual(900.0000000000004m, quote.AssetsTotalAfter);
    }

    [TestMethod]
    public void Default_AvailableAboveTheTotalBeyondDustIsRefused()
    {
        // A vault whose available assets leave less lent out than the exposure: after the default
        // AssetsAvailable (950) is above AssetsTotal (900) by far more than dust.
        LoanManageQuote quote = LoanManagement.Default(
            Loan(totalValue: "100", managementFee: "0", principal: "100"),
            Broker("1000", 0, 0, "0"),
            Vault(Usd, "1000", "950"),
            Fixed);

        Assert.AreEqual("tecINTERNAL", quote.Refusal);
        Assert.AreEqual(1000m, quote.AssetsTotalAfter);
    }

    [TestMethod]
    public void AFixedPrecisionVaultIsNotSupported()
    {
        LOVault fixedPrecision = new LOVault { Index = VaultId, Asset = Xrp, AssetsTotal = 100, AssetsAvailable = 40, LEVersion = 2 };
        LOLoanBroker broker = Broker("20000001", 10_000, 33_333, "5000000");

        Assert.ThrowsExactly<NotSupportedException>(() => LoanManagement.Exposure(Loan(), fixedPrecision));
        Assert.ThrowsExactly<NotSupportedException>(() => LoanManagement.Impair(Loan(), fixedPrecision, Due.AddSeconds(1), Fixed));
        Assert.ThrowsExactly<NotSupportedException>(() => LoanManagement.Unimpair(Loan(flags: LoanFlags.lsfLoanImpaired), fixedPrecision, Due, Fixed));
        Assert.ThrowsExactly<NotSupportedException>(() => LoanManagement.Default(Loan(), broker, fixedPrecision, Fixed));
    }

    [TestMethod]
    public void Default_ChecksTheGracePeriodOnlyWhenATimeIsGiven()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 33_333, "5000000");

        Assert.AreEqual("tecTOO_SOON", LoanManagement.Default(Loan(), broker, XrpVault(), Fixed, Due.AddSeconds(Grace)).Refusal);
        Assert.IsTrue(LoanManagement.Default(Loan(), broker, XrpVault(), Legacy, Due.AddSeconds(Grace)).IsAccepted);
        Assert.IsTrue(LoanManagement.Default(Loan(), broker, XrpVault(), Fixed).IsAccepted);

        LoanManageQuote defaulted = LoanManagement.Default(Loan(flags: LoanFlags.lsfLoanDefault), broker, XrpVault(), Fixed);
        Assert.AreEqual("tecNO_PERMISSION", defaulted.Refusal);
        Assert.AreEqual(0m, defaulted.DefaultCovered);
        Assert.AreEqual(100_000_000m, defaulted.AssetsTotalAfter);
        Assert.AreEqual(5_000_000m, defaulted.CoverAvailableAfter);
    }

    [TestMethod]
    public void Default_AVaultThatIsNotTheBrokersIsRejected()
    {
        LOLoanBroker broker = Broker("20000001", 10_000, 33_333, "5000000");
        LOVault other = new LOVault { Index = LoanId, Asset = Xrp };
        Assert.ThrowsExactly<ArgumentException>(() => LoanManagement.Default(Loan(), broker, other, Fixed));
    }

    private static Meta Metadata(string affectedNodes) =>
        JsonSerializer.Deserialize<Meta>($"{{\"AffectedNodes\":[{affectedNodes}],\"TransactionIndex\":0,\"TransactionResult\":\"tesSUCCESS\"}}", XrplJsonOptions.Default);

    private static string LoanNode(uint flagsBefore, uint flagsAfter) =>
        $"{{\"ModifiedNode\":{{\"LedgerEntryType\":\"Loan\",\"LedgerIndex\":\"{LoanId}\","
        + $"\"FinalFields\":{{\"Flags\":{flagsAfter},\"LoanBrokerID\":\"{BrokerId}\",\"PaymentRemaining\":3}},"
        + $"\"PreviousFields\":{{\"Flags\":{flagsBefore}}}}}}}";

    private static string VaultNode(string final, string previous) =>
        $"{{\"ModifiedNode\":{{\"LedgerEntryType\":\"Vault\",\"LedgerIndex\":\"{VaultId}\","
        + $"\"FinalFields\":{{\"Asset\":{{\"currency\":\"XRP\"}},{final}}},\"PreviousFields\":{{{previous}}}}}}}";

    private static string BrokerNode(string final, string previous) =>
        $"{{\"ModifiedNode\":{{\"LedgerEntryType\":\"LoanBroker\",\"LedgerIndex\":\"{BrokerId}\","
        + $"\"FinalFields\":{{\"VaultID\":\"{VaultId}\",{final}}},\"PreviousFields\":{{{previous}}}}}}}";

    private static LoanManage Manage(LoanManageFlags? flags) => new LoanManage { Account = Owner, LoanID = LoanId, Flags = flags };

    [TestMethod]
    public void Outcome_Default_ReadsTheVaultAndBrokerDeltas()
    {
        Meta meta = Metadata(string.Join(",",
            LoanNode(0, (uint)LoanFlags.lsfLoanDefault),
            VaultNode("\"AssetsTotal\":\"90216661\",\"AssetsAvailable\":\"40666661\"", "\"AssetsTotal\":\"100000000\",\"AssetsAvailable\":\"40000000\""),
            BrokerNode("\"DebtTotal\":\"9550001\",\"CoverAvailable\":\"4333339\"", "\"DebtTotal\":\"20000001\",\"CoverAvailable\":\"5000000\"")));

        LoanManageOutcome outcome = LoanManageOutcome.FromMetadata(Manage(LoanManageFlags.tfLoanDefault), meta);

        Assert.AreEqual(LoanManageAction.Default, outcome.Action);
        Assert.IsTrue(outcome.IsDefaulted);
        Assert.IsFalse(outcome.IsImpaired);
        Assert.AreEqual(-9_783_339m, outcome.AssetsTotalChange);
        Assert.AreEqual(666_661m, outcome.AssetsAvailableChange);
        Assert.AreEqual(0m, outcome.LossUnrealizedChange);
        Assert.AreEqual(-10_450_000m, outcome.DebtTotalChange);
        Assert.AreEqual(-666_661m, outcome.CoverAvailableChange);
        Assert.IsNotNull(outcome.BrokerAfter);
    }

    [TestMethod]
    public void Outcome_DefaultOfAnImpairedLoan_KeepsTheImpairedFlag()
    {
        // defaultLoan sets lsfLoanDefault and leaves lsfLoanImpaired; only unimpairLoan clears it.
        Meta meta = Metadata(string.Join(",",
            LoanNode((uint)LoanFlags.lsfLoanImpaired, (uint)(LoanFlags.lsfLoanImpaired | LoanFlags.lsfLoanDefault)),
            VaultNode("\"AssetsTotal\":\"90216661\",\"AssetsAvailable\":\"40666661\"",
                "\"AssetsTotal\":\"100000000\",\"AssetsAvailable\":\"40000000\",\"LossUnrealized\":\"10450000\""),
            BrokerNode("\"DebtTotal\":\"9550001\",\"CoverAvailable\":\"4333339\"", "\"DebtTotal\":\"20000001\",\"CoverAvailable\":\"5000000\"")));

        LoanManageOutcome outcome = LoanManageOutcome.FromMetadata(Manage(LoanManageFlags.tfLoanDefault), meta);

        Assert.AreEqual(LoanManageAction.Default, outcome.Action);
        Assert.IsTrue(outcome.IsDefaulted);
        Assert.IsTrue(outcome.IsImpaired);
        Assert.AreEqual(-10_450_000m, outcome.LossUnrealizedChange);
        Assert.AreEqual(-9_783_339m, outcome.AssetsTotalChange);
    }

    [TestMethod]
    public void Outcome_Default_AvailableAssetsRisingFromZero()
    {
        // A fully lent vault carries no AssetsAvailable; the default adds it, so it is in FinalFields
        // only. The broker's cover moved, which says AssetsAvailable did too.
        Meta meta = Metadata(string.Join(",",
            LoanNode(0, (uint)LoanFlags.lsfLoanDefault),
            VaultNode("\"AssetsTotal\":\"100\",\"AssetsAvailable\":\"50\"", "\"AssetsTotal\":\"150\""),
            BrokerNode("\"CoverAvailable\":\"150\"", "\"DebtTotal\":\"100\",\"CoverAvailable\":\"200\"")));

        LoanManageOutcome outcome = LoanManageOutcome.FromMetadata(Manage(LoanManageFlags.tfLoanDefault), meta);

        Assert.AreEqual(50m, outcome.AssetsAvailableChange);
        Assert.AreEqual(-50m, outcome.AssetsTotalChange);
        Assert.AreEqual(-100m, outcome.DebtTotalChange);
        Assert.AreEqual(-50m, outcome.CoverAvailableChange);
    }

    [TestMethod]
    public void Outcome_ImpairAndUnimpair_ReadTheUnrealizedLoss()
    {
        // The first impairment adds LossUnrealized; the unimpairment that clears it removes the field.
        Meta impaired = Metadata(string.Join(",",
            LoanNode(0, (uint)LoanFlags.lsfLoanImpaired),
            VaultNode("\"AssetsTotal\":\"100000000\",\"LossUnrealized\":\"10450000\"", "")));
        LoanManageOutcome impair = LoanManageOutcome.FromMetadata(Manage(LoanManageFlags.tfLoanImpair), impaired);
        Assert.AreEqual(LoanManageAction.Impair, impair.Action);
        Assert.IsTrue(impair.IsImpaired);
        Assert.AreEqual(10_450_000m, impair.LossUnrealizedChange);
        Assert.AreEqual(0m, impair.AssetsTotalChange);
        Assert.AreEqual(0m, impair.DebtTotalChange);
        Assert.IsNull(impair.BrokerAfter);

        Meta unimpaired = Metadata(string.Join(",",
            LoanNode((uint)LoanFlags.lsfLoanImpaired, 0),
            VaultNode("\"AssetsTotal\":\"100000000\"", "\"LossUnrealized\":\"10450000\"")));
        LoanManageOutcome unimpair = LoanManageOutcome.FromMetadata(Manage(LoanManageFlags.tfLoanUnimpair), unimpaired);
        Assert.AreEqual(LoanManageAction.Unimpair, unimpair.Action);
        Assert.IsFalse(unimpair.IsImpaired);
        Assert.AreEqual(-10_450_000m, unimpair.LossUnrealizedChange);
    }

    [TestMethod]
    public void Outcome_WithoutAnActionFlag_ChangesNothing()
    {
        LoanManageOutcome outcome = LoanManageOutcome.FromMetadata(Manage(null), Metadata(""));
        Assert.AreEqual(LoanManageAction.None, outcome.Action);
        Assert.AreEqual(LoanId, outcome.LoanId);
        Assert.IsNull(outcome.LoanFlagsAfter);
        Assert.AreEqual(0m, outcome.AssetsTotalChange);

        Assert.ThrowsExactly<ArgumentException>(() => LoanManageOutcome.FromMetadata(Manage(LoanManageFlags.tfLoanImpair), Metadata("")));
    }
}
