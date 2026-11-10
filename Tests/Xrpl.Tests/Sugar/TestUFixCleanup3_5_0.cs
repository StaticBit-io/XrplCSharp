using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Sugar;

/// <summary>
/// The behaviour rippled 3.5.0 gates on <c>fixCleanup3_5_0</c>, where the SDK reproduces it:
/// vault withdrawals that pay the transfer fee (rippled #8267), <c>LoanSet</c> value fields out of
/// the asset's range (#8527), <c>ceilOut</c> on an XRP product past the ledger's range (#8480)
/// and <c>divRound</c> with an MPT operand (#8453).
/// </summary>
[TestClass]
public class TestUFixCleanup3_5_0
{
    private const uint QualityOne = 1_000_000_000;
    private const string Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
    private const string Holder = "rPEPPER7kfTD9w2To4CQk6UCfuHM9c6GDY";
    private const string Other = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = Issuer };
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static readonly LedgerRules Fixed = new LedgerRules { FixCleanup3_5_0 = true };
    private static readonly LedgerRules Legacy = new LedgerRules { FixCleanup3_5_0 = false };

    private static Dictionary<string, FeatureInfo> Features(params (string Name, bool Enabled)[] features)
    {
        Dictionary<string, FeatureInfo> result = new Dictionary<string, FeatureInfo>();
        for (int i = 0; i < features.Length; i++)
            result[i.ToString("X64")] = new FeatureInfo { Name = features[i].Name, Enabled = features[i].Enabled };

        return result;
    }

    [TestMethod]
    public void LedgerRules_ReadsTheAmendmentAndWhetherTheNodeKnowsIt()
    {
        LedgerRules enabled = LedgerRules.FromFeatures(new ServerFeatures { Features = Features(("fixCleanup3_5_0", true)) });
        Assert.IsTrue(enabled.FixCleanup3_5_0);
        Assert.AreEqual(true, enabled.FixCleanup3_5_0Known);

        // A 3.5.0 node with the amendment not yet enabled: off in a transaction, on outside one.
        LedgerRules pending = LedgerRules.FromFeatures(new ServerFeatures { Features = Features(("fixCleanup3_5_0", false)) });
        Assert.IsFalse(pending.CurrentFixCleanup3_5_0);
        Assert.IsTrue(pending.OutsideOfTransaction().CurrentFixCleanup3_5_0);

        // A node before 3.5.0 does not list it and runs the old code everywhere.
        LedgerRules older = LedgerRules.FromFeatures(new ServerFeatures { Features = Features(("fixCleanup3_4_0", true)) });
        Assert.IsFalse(older.FixCleanup3_5_0);
        Assert.IsFalse(older.OutsideOfTransaction().CurrentFixCleanup3_5_0);
    }

    [TestMethod]
    public void LedgerRules_BuiltByHand_FollowTheFlagOutsideATransaction()
    {
        Assert.IsTrue(new LedgerRules().OutsideOfTransaction().CurrentFixCleanup3_5_0);
        Assert.IsFalse(new LedgerRules { FixCleanup3_5_0 = false }.OutsideOfTransaction().CurrentFixCleanup3_5_0);
        Assert.IsFalse(new LedgerRules { FixCleanup3_5_0 = false }.WithMPTokensV2().OutsideOfTransaction().CurrentFixCleanup3_5_0);
    }

    [TestMethod]
    public void WithdrawalTransferRate_OnlyBetweenTwoHolders()
    {
        const uint rate = 1_250_000_000;

        Assert.AreEqual(rate, VaultShares.WithdrawalTransferRate(Usd, Holder, Other, rate, Fixed));
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Usd, Holder, null, rate, Fixed), "to itself");
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Usd, Holder, Holder, rate, Fixed), "to itself, named");
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Usd, Issuer, Other, rate, Fixed), "from the issuer");
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Usd, Holder, Issuer, rate, Fixed), "to the issuer");
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Xrp, Holder, Other, rate, Fixed), "XRP");
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Usd, Holder, Other, 0, Fixed), "no rate set");
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(Usd, Holder, Other, rate, Legacy), "before the amendment");
    }

    [TestMethod]
    public void WithdrawalTransferRate_ReadsAnMptIssuer()
    {
        string issuer = XrplWallet.Generate().ClassicAddress;
        IssuedCurrency mpt = new IssuedCurrency { MptIssuanceId = ParseMPTID.GenerateMPTokenIssuanceID(1, issuer) };

        Assert.AreEqual(1_010_000_000u, VaultShares.WithdrawalTransferRate(mpt, Holder, Other, 1_010_000_000, Fixed));
        Assert.AreEqual(QualityOne, VaultShares.WithdrawalTransferRate(mpt, Holder, issuer, 1_010_000_000, Fixed));
    }

    private static LOVault UsdVault() => new LOVault
    {
        Asset = Usd,
        AssetsTotal = 1000,
        AssetsAvailable = 1000,
    };

    /// <summary>
    /// <c>VaultWithdraw</c> by amount: the vault burns shares for the amount plus the fee
    /// (<c>addTransferFee</c>), so the destination receives the amount asked for.
    /// </summary>
    [TestMethod]
    public void WithdrawAssets_ThirdParty_BurnsSharesForTheFee()
    {
        VaultQuote quote = VaultShares.WithdrawAssets(UsdVault(), 1000, 500, 100, 1_250_000_000, Fixed);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(125m, quote.Shares);
        Assert.AreEqual(125m, quote.Assets);
        Assert.AreEqual(100m, quote.AssetsDelivered);

        VaultQuote legacy = VaultShares.WithdrawAssets(UsdVault(), 1000, 500, 100, 1_250_000_000, Legacy);
        Assert.AreEqual(100m, legacy.Shares, "no fee before the amendment");
        Assert.AreEqual(100m, legacy.AssetsDelivered);
    }

    /// <summary><c>VaultWithdraw</c> by shares: the fee comes out of the payout, rounded down (<c>subtractTransferFee</c>).</summary>
    [TestMethod]
    public void RedeemShares_ThirdParty_DeliversThePayoutLessTheFee()
    {
        VaultQuote quote = VaultShares.RedeemShares(UsdVault(), 1000, 500, 10, 1_200_000_000, Fixed);

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(10m, quote.Shares);
        Assert.AreEqual(10m, quote.Assets);
        Assert.AreEqual(8.333333333333333m, quote.AssetsDelivered);
    }

    [TestMethod]
    public void RedeemShares_FeeLeavesNothing_IsPrecisionLoss()
    {
        string issuer = XrplWallet.Generate().ClassicAddress;
        LOVault vault = new LOVault
        {
            Asset = new IssuedCurrency { MptIssuanceId = ParseMPTID.GenerateMPTokenIssuanceID(1, issuer) },
            AssetsTotal = 10,
            AssetsAvailable = 10,
        };

        VaultQuote quote = VaultShares.RedeemShares(vault, 10, 5, 1, 1_500_000_000, Fixed);

        Assert.AreEqual("tecPRECISION_LOSS", quote.Refusal, quote.RefusalReason);
    }

    [TestMethod]
    public void Deposit_DeliversWhatItTakes()
    {
        VaultQuote quote = VaultShares.Deposit(UsdVault(), 1000, 40, rules: Fixed);

        Assert.AreEqual(quote.Assets, quote.AssetsDelivered);
    }

    [TestMethod]
    public void WithdrawAssets_RateBelowParity_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => VaultShares.WithdrawAssets(UsdVault(), 1000, 500, 100, 999_999_999, Fixed));
    }

    /// <summary>
    /// <c>LoanSet::preclaim</c>: a value field beyond what XRP holds is <c>tecPRECISION_LOSS</c>
    /// under the amendment; before it, building the amount throws, which the node reports as
    /// <c>tefEXCEPTION</c>.
    /// </summary>
    [TestMethod]
    public void LoanSet_ValueOutOfRange()
    {
        LOVault vault = new LOVault { Asset = Xrp, AssetsTotal = 1_000_000_000, AssetsAvailable = 1_000_000_000 };
        LOLoanBroker broker = new LOLoanBroker();
        LoanSet loanSet = new LoanSet
        {
            Account = Holder,
            PrincipalRequested = 1000,
            LoanOriginationFee = XrplNumber.Parse("200000000000000000"),
        };

        Assert.AreEqual("tecPRECISION_LOSS", LoanOrigination.Compute(loanSet, vault, broker, options: Fixed).Refusal);
        Assert.AreEqual("tefEXCEPTION", LoanOrigination.Compute(loanSet, vault, broker, options: Legacy).Refusal);
    }

    /// <summary>
    /// <c>Quality::ceilOut</c> (rippled #8480): an XRP input past the ledger's range is clamped to
    /// the offer's input under the amendment, instead of aborting the step.
    /// </summary>
    [TestMethod]
    public void CeilOut_XrpProductOutOfRange_ClampsToTheInput()
    {
        // A book quality far worse than the amounts it is applied to: 10^18 drops a unit, past
        // the 10^17 drops XRP holds but within 64 bits, so STAmount refuses it with a
        // runtime_error rather than Number with an overflow_error.
        XrplQuality quality = XrplQuality.FromAmounts(
            XrplAmount.Parse(Xrp, "100000000000000000"), XrplAmount.Parse(Usd, "0.1"));
        XrplAmount @in = XrplAmount.Parse(Xrp, "1000");
        XrplAmount @out = XrplAmount.Parse(Usd, "10");
        XrplAmount limit = XrplAmount.Parse(Usd, "1");

        (XrplAmount In, XrplAmount Out) result = quality.CeilOut(@in, @out, limit, Fixed);
        Assert.AreEqual(@in, result.In);
        Assert.AreEqual(limit, result.Out);

        Assert.Throws<OverflowException>(() => quality.CeilOut(@in, @out, limit, Legacy));

        // Past 64 bits it is Number's overflow_error, which tryMulRound rethrows.
        XrplQuality worse = XrplQuality.FromAmounts(
            XrplAmount.Parse(Xrp, "100000000000000000"), XrplAmount.Parse(Usd, "1e-10"));
        Assert.Throws<OverflowException>(() => worse.CeilOut(@in, @out, limit, Fixed));
    }

    /// <summary>
    /// <c>divRound</c>: the legacy path scales an MPT numerator by 10^17 and overflows; under the
    /// amendment any MPT operand goes through <c>Number</c>, MPTokensV2 or not.
    /// </summary>
    [TestMethod]
    public void DivRound_LargeMptNumerator_GoesThroughNumber()
    {
        string issuer = XrplWallet.Generate().ClassicAddress;
        IssuedCurrency mpt = new IssuedCurrency { MptIssuanceId = ParseMPTID.GenerateMPTokenIssuanceID(1, issuer) };
        XrplAmount num = XrplAmount.Parse(mpt, "9000000000000000000");
        XrplAmount den = XrplAmount.Parse(Usd, "1");

        XrplAmount quotient = XrplAmountMath.DivRound(num, den, Usd, roundUp: false, new LedgerRules { MPTokensV2 = false, FixCleanup3_5_0 = true });
        Assert.AreEqual(XrplAmount.Parse(Usd, "9e18"), quotient);

        Assert.Throws<OverflowException>(
            () => XrplAmountMath.DivRound(num, den, Usd, roundUp: false, new LedgerRules { MPTokensV2 = false, FixCleanup3_5_0 = false }));
    }
}
