using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// The integer-arithmetic hardening of rippled 3.4.1 in the payment engine: <c>checkedAdd</c>
/// (<c>MathUtilities.h</c>, its <c>static_assert</c>s replayed on MPT and XRP amounts),
/// <c>checkedStepAddOpt</c> behind the strand flow's <c>sum</c>, which turns an overflowing total
/// into <c>tecPATH_DRY</c>, and <c>checkedStepAdd</c> behind <c>BookStep</c>'s, which fails the
/// strand with a <c>FlowException</c>.
/// </summary>
[TestClass]
public class TestUCheckedStepAdd
{
    private static readonly IssuedCurrency Mpt = new IssuedCurrency
    {
        MptIssuanceId = ParseMPTID.GenerateMPTokenIssuanceID(1, XrplWallet.Generate().ClassicAddress),
    };

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };

    private static readonly LedgerRules Rules = new LedgerRules();

    private static XrplAmount Units(IssuedCurrency asset, long units) => XrplAmount.FromUnits(asset, XrplAmount.KindOf(asset), units);

    private static long? CheckedAdd(IssuedCurrency asset, long a, long b)
    {
        XrplAmount? sum = StepMath.CheckedAdd(Units(asset, a), Units(asset, b), Rules);
        if (sum is not { } total)
            return null;

        return total.IsNegative ? -(long)total.StMantissa : (long)total.StMantissa;
    }

    [TestMethod]
    public void TestUCheckedAdd_RepresentableSums()
    {
        foreach (IssuedCurrency asset in new[] { Mpt, Xrp })
        {
            Assert.AreEqual(0L, CheckedAdd(asset, 0, 0));
            Assert.AreEqual(0L, CheckedAdd(asset, 1, -1));
            Assert.AreEqual(-3L, CheckedAdd(asset, -5, 2));
            Assert.AreEqual(long.MaxValue, CheckedAdd(asset, long.MaxValue - 1, 1));
            Assert.AreEqual(0L, CheckedAdd(asset, long.MaxValue, -long.MaxValue));
            Assert.AreEqual(-long.MaxValue, CheckedAdd(asset, -long.MaxValue + 1, -1));
        }
    }

    [TestMethod]
    public void TestUCheckedAdd_OverflowIsNull()
    {
        foreach (IssuedCurrency asset in new[] { Mpt, Xrp })
        {
            Assert.IsNull(CheckedAdd(asset, long.MaxValue, 1));
            Assert.IsNull(CheckedAdd(asset, long.MaxValue, long.MaxValue));
            Assert.IsNull(CheckedAdd(asset, -long.MaxValue, -2));
            Assert.IsNull(CheckedAdd(asset, -long.MaxValue, -long.MaxValue));
        }
    }

    [TestMethod]
    public void TestUTrySum_OverflowingTotalIsNull()
    {
        long half = long.MaxValue / 2;
        List<XrplAmount> fits = new List<XrplAmount> { Units(Mpt, half), Units(Mpt, half), Units(Mpt, 1) };
        Assert.AreEqual((ulong)long.MaxValue, StepMath.TrySum(fits, Mpt, Rules)!.Value.StMantissa);

        List<XrplAmount> overflows = new List<XrplAmount> { Units(Mpt, half), Units(Mpt, half), Units(Mpt, 2) };
        Assert.IsNull(StepMath.TrySum(overflows, Mpt, Rules));

        Assert.IsTrue(StepMath.TrySum(new List<XrplAmount>(), Mpt, Rules)!.Value.IsZero);
    }

    [TestMethod]
    public void TestUSum_OverflowFailsTheStrandAsPathDry()
    {
        List<XrplAmount> overflows = new List<XrplAmount> { Units(Mpt, long.MaxValue), Units(Mpt, 1) };

        FlowFailedException failure = Assert.Throws<FlowFailedException>(() => StepMath.Sum(overflows, Mpt, Rules));
        Assert.AreEqual("tecPATH_DRY", failure.Result);
    }

    [TestMethod]
    public void TestUSum_IssuedCurrencyIsUnchecked()
    {
        List<XrplAmount> amounts = new List<XrplAmount>
        {
            XrplAmount.Parse(Usd, "9999999999999999e80"),
            XrplAmount.Parse(Usd, "1"),
        };

        XrplAmount? sum = StepMath.TrySum(amounts, Usd, Rules);
        Assert.IsNotNull(sum);
        Assert.AreEqual(XrplAmount.Parse(Usd, "9999999999999999e80"), sum.Value);
    }
}
