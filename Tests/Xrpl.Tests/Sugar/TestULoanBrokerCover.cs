using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestULoanBrokerCover
{
    private static readonly LedgerRules Fixed = new LedgerRules();
    private static readonly LedgerRules Legacy = new LedgerRules { FixCleanup3_2_0 = false };

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };
    private static readonly IssuedCurrency Mpt = new IssuedCurrency { MptIssuanceId = "00000001A407AF5856CCF3C42619DAA925813FC955C72983" };

    private static LOLoanBroker Broker(string cover, string debt, uint rate) => new LOLoanBroker
    {
        CoverAvailable = XrplNumber.Parse(cover),
        DebtTotal = XrplNumber.Parse(debt),
        CoverRateMinimum = rate,
    };

    private static LOVault Vault(IssuedCurrency asset, string assetsTotal) => new LOVault
    {
        Asset = asset,
        AssetsTotal = XrplNumber.Parse(assetsTotal),
    };

    // An IOU vault whose AssetsTotal (2000100.5, exponent -9 as an amount) is coarser than the
    // broker's DebtTotal (1234.56789, exponent -12). 12.345% of the debt is 152.4074060205.
    private static LOLoanBroker IouBroker() => Broker("500", "1234.56789", 12_345);

    private static LOVault IouVault() => Vault(Usd, "2000100.5");

    [TestMethod]
    public void MinimumCover_Iou_RoundsToTheVaultScaleUnderTheFixAndToTheDebtScaleBefore()
    {
        // fixCleanup3_2_0: minimumBrokerCover rounds up at the AssetsTotal scale 10^-9. Before it,
        // LoanBrokerCoverWithdraw rounds at the DebtTotal scale 10^-12, which holds the product exactly.
        Assert.AreEqual(152.407406021m, LoanBrokerCover.MinimumCover(IouBroker(), IouVault(), Fixed));
        Assert.AreEqual(152.4074060205m, LoanBrokerCover.MinimumCover(IouBroker(), IouVault(), Legacy));
    }

    [TestMethod]
    public void MaxWithdrawable_Iou_IsTheCoverAboveTheMinimumOnEitherSideOfTheFix()
    {
        Assert.AreEqual(347.592593979m, LoanBrokerCover.MaxWithdrawable(IouBroker(), IouVault(), Fixed));
        Assert.AreEqual(347.5925939795m, LoanBrokerCover.MaxWithdrawable(IouBroker(), IouVault(), Legacy));
    }

    [TestMethod]
    public void CheckWithdraw_Iou_TheLegacyMaximumIsRefusedUnderTheFix()
    {
        // A wallet that computes the maximum the old way has its withdrawal refused once the
        // amendment is on: 500 - 347.5925939795 leaves 152.4074060205 < 152.407406021.
        XrplNumber legacyMaximum = XrplNumber.Parse("347.5925939795");

        LoanBrokerCoverQuote legacy = LoanBrokerCover.CheckWithdraw(IouBroker(), IouVault(), legacyMaximum, Legacy);
        Assert.IsTrue(legacy.IsAccepted, legacy.RefusalReason);
        Assert.AreEqual(152.4074060205m, legacy.CoverAfter);

        LoanBrokerCoverQuote refused = LoanBrokerCover.CheckWithdraw(IouBroker(), IouVault(), legacyMaximum, Fixed);
        Assert.AreEqual("tecINSUFFICIENT_FUNDS", refused.Refusal);
        Assert.AreEqual(0m, refused.Amount);
        Assert.AreEqual(500m, refused.CoverAfter);
        Assert.AreEqual(152.407406021m, refused.MinimumCover);

        LoanBrokerCoverQuote accepted = LoanBrokerCover.CheckWithdraw(IouBroker(), IouVault(), XrplNumber.Parse("347.592593979"), Fixed);
        Assert.IsTrue(accepted.IsAccepted, accepted.RefusalReason);
        Assert.AreEqual(347.592593979m, accepted.Amount);
        Assert.AreEqual(500m, accepted.CoverBefore);
        Assert.AreEqual(152.407406021m, accepted.CoverAfter);
    }

    [TestMethod]
    public void CheckWithdraw_MoreThanTheCover_IsInsufficientFunds()
    {
        LoanBrokerCoverQuote quote = LoanBrokerCover.CheckWithdraw(Broker("500", "0", 0), IouVault(), 501, Fixed);

        Assert.AreEqual("tecINSUFFICIENT_FUNDS", quote.Refusal);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    public void CheckWithdraw_NotPositive_IsBadAmount(string amount)
    {
        Assert.AreEqual("temBAD_AMOUNT", LoanBrokerCover.CheckWithdraw(IouBroker(), IouVault(), XrplNumber.Parse(amount), Fixed).Refusal);
    }

    [TestMethod]
    public void CheckWithdraw_BelowTheCoverScale_IsPrecisionLossUnderTheFixOnly()
    {
        // 1234.5 is held at exponent -12; 4e-13 rounds to zero there, 6e-13 does not.
        LOLoanBroker broker = Broker("1234.5", "0", 0);

        Assert.AreEqual("tecPRECISION_LOSS", LoanBrokerCover.CheckWithdraw(broker, IouVault(), XrplNumber.Parse("4e-13"), Fixed).Refusal);
        Assert.IsTrue(LoanBrokerCover.CheckWithdraw(broker, IouVault(), XrplNumber.Parse("6e-13"), Fixed).IsAccepted);
        Assert.IsTrue(LoanBrokerCover.CheckWithdraw(broker, IouVault(), XrplNumber.Parse("4e-13"), Legacy).IsAccepted);
    }

    [TestMethod]
    public void CheckDeposit_Iou_RoundsDownToTheCoverScaleUnderTheFix()
    {
        LOLoanBroker broker = Broker("1234.5", "0", 0);
        XrplNumber amount = XrplNumber.Parse("1.5e-12");

        LoanBrokerCoverQuote fixedQuote = LoanBrokerCover.CheckDeposit(broker, IouVault(), amount, Fixed);
        Assert.IsTrue(fixedQuote.IsAccepted, fixedQuote.RefusalReason);
        Assert.AreEqual(0.000000000001m, fixedQuote.Amount);
        Assert.AreEqual(1234.500000000001m, fixedQuote.CoverAfter);

        LoanBrokerCoverQuote legacyQuote = LoanBrokerCover.CheckDeposit(broker, IouVault(), amount, Legacy);
        Assert.AreEqual(0.0000000000015m, legacyQuote.Amount);
        Assert.AreEqual(1234.5000000000015m, legacyQuote.CoverAfter);

        Assert.AreEqual("tecPRECISION_LOSS", LoanBrokerCover.CheckDeposit(broker, IouVault(), XrplNumber.Parse("9e-13"), Fixed).Refusal);
        Assert.AreEqual("temBAD_AMOUNT", LoanBrokerCover.CheckDeposit(broker, IouVault(), 0, Fixed).Refusal);
    }

    [TestMethod]
    public void CheckClawback_Iou_TakesEverythingAboveTheMinimum()
    {
        Assert.AreEqual(347.592593979m, LoanBrokerCover.MaxClawback(IouBroker(), IouVault(), Fixed));
        Assert.AreEqual(347.5925939795m, LoanBrokerCover.MaxClawback(IouBroker(), IouVault(), Legacy));

        // Zero and an amount above the maximum both mean everything above the minimum.
        Assert.AreEqual(347.592593979m, LoanBrokerCover.CheckClawback(IouBroker(), IouVault(), 0, Fixed).Amount);
        Assert.AreEqual(347.592593979m, LoanBrokerCover.CheckClawback(IouBroker(), IouVault(), 1_000, Fixed).Amount);

        LoanBrokerCoverQuote partial = LoanBrokerCover.CheckClawback(IouBroker(), IouVault(), 100, Fixed);
        Assert.AreEqual(100m, partial.Amount);
        Assert.AreEqual(400m, partial.CoverAfter);
    }

    [TestMethod]
    public void CheckClawback_AtTheMinimum_IsInsufficientFunds()
    {
        LoanBrokerCoverQuote quote = LoanBrokerCover.CheckClawback(Broker("152.407406021", "1234.56789", 12_345), IouVault(), rules: Fixed);

        Assert.AreEqual("tecINSUFFICIENT_FUNDS", quote.Refusal);
        Assert.AreEqual(0m, LoanBrokerCover.MaxClawback(Broker("152.407406021", "1234.56789", 12_345), IouVault(), Fixed));
    }

    [TestMethod]
    public void MinimumCover_ScaleIsTakenInTheUpwardGuard()
    {
        // AssetsTotal 9999999999999999.4 has 17 digits. As a 16-digit amount it is
        // 9999999999999999 (exponent 0) to nearest, but 10^16 (exponent 1) rounded up, which is
        // how minimumBrokerCover takes it: 12.345% of 1000 is 123.45, rounded up to a multiple of 10.
        LOVault vault = new LOVault { Asset = Usd, AssetsTotal = new XrplNumber(99_999_999_999_999_994L, -1) };

        Assert.AreEqual(130m, LoanBrokerCover.MinimumCover(Broker("1000", "1000", 12_345), vault, Fixed));
    }

    // 12.345% of 1,234,567 is 152,407.29615.
    private static LOLoanBroker IntegralBroker() => Broker("1000000", "1234567", 12_345);

    [TestMethod]
    public void Xrp_TheMinimumIsAWholeDropOnEitherSideOfTheFix()
    {
        LOVault vault = Vault(Xrp, "50000000");

        foreach (LedgerRules rules in new[] { Fixed, Legacy })
        {
            Assert.AreEqual(152_408m, LoanBrokerCover.MinimumCover(IntegralBroker(), vault, rules));
            Assert.AreEqual(847_592m, LoanBrokerCover.MaxWithdrawable(IntegralBroker(), vault, rules));
            Assert.IsTrue(LoanBrokerCover.CheckWithdraw(IntegralBroker(), vault, 847_592, rules).IsAccepted);
            Assert.AreEqual("tecINSUFFICIENT_FUNDS", LoanBrokerCover.CheckWithdraw(IntegralBroker(), vault, 847_593, rules).Refusal);
        }

        Assert.AreEqual("tecNO_PERMISSION", LoanBrokerCover.CheckClawback(IntegralBroker(), vault, rules: Fixed).Refusal);
        Assert.AreEqual("temBAD_AMOUNT", LoanBrokerCover.CheckClawback(IntegralBroker(), vault, 1, Fixed).Refusal);
        Assert.AreEqual(0m, LoanBrokerCover.MaxClawback(IntegralBroker(), vault, Fixed));
    }

    [TestMethod]
    public void Mpt_TheLegacyClawbackKeepsTheUnroundedMinimum()
    {
        LOVault vault = Vault(Mpt, "50000000");

        Assert.AreEqual(152_408m, LoanBrokerCover.MinimumCover(IntegralBroker(), vault, Fixed));
        Assert.AreEqual(152_408m, LoanBrokerCover.MinimumCover(IntegralBroker(), vault, Legacy));
        Assert.AreEqual(847_592m, LoanBrokerCover.MaxWithdrawable(IntegralBroker(), vault, Legacy));

        // Before the fix the clawback keeps 152,407.29615 unrounded, and the 847,592.70385 above it
        // becomes a whole unit to nearest: the issuer takes 847,593, one more than under the fix.
        Assert.AreEqual(847_592m, LoanBrokerCover.MaxClawback(IntegralBroker(), vault, Fixed));
        LoanBrokerCoverQuote legacy = LoanBrokerCover.CheckClawback(IntegralBroker(), vault, rules: Legacy);
        Assert.AreEqual(847_593m, legacy.Amount);
        Assert.AreEqual(152_407.29615m, legacy.MinimumCover);
        Assert.AreEqual(152_407m, legacy.CoverAfter);
    }

    [TestMethod]
    public void MaxWithdrawable_BelowTheMinimum_IsZero()
    {
        Assert.AreEqual(0m, LoanBrokerCover.MaxWithdrawable(Broker("100", "1234.56789", 12_345), IouVault(), Fixed));
    }

    [TestMethod]
    public void NoDebt_EverythingCanBeWithdrawn()
    {
        LOLoanBroker broker = new LOLoanBroker { CoverAvailable = 500 };

        Assert.AreEqual(0m, LoanBrokerCover.MinimumCover(broker, IouVault()));
        Assert.AreEqual(500m, LoanBrokerCover.MaxWithdrawable(broker, IouVault()));
    }

    [TestMethod]
    public void AnotherBrokersVault_IsRejected()
    {
        LOLoanBroker broker = new LOLoanBroker { VaultID = new string('A', 64), CoverAvailable = 500 };
        LOVault vault = new LOVault { Asset = Usd, Index = new string('B', 64) };

        Assert.Throws<ArgumentException>(() => LoanBrokerCover.MinimumCover(broker, vault));
    }
}
