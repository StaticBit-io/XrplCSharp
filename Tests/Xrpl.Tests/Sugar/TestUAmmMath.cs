using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using Xrpl.Sugar;

namespace XrplTests.Xrpl.Sugar;

/// <summary>
/// AMM deposit and withdrawal arithmetic, checked against what rippled computes - issue #133.
/// </summary>
/// <remarks>
/// The formulas are equations 3 and 7 from rippled's <c>AMMHelpers.cpp</c>, and the point of taking
/// them from there rather than from the widely quoted approximation is measurable: see
/// <see cref="TestUTheCirculatingApproximationIsWrongOnceThereIsAFee"/>.
/// </remarks>
[TestClass]
public class TestUAmmMath
{
    /// <summary>
    /// The figure from the report, and the reason this class exists.
    /// </summary>
    /// <remarks>
    /// A deposit the size of the pool at a 1% fee. rippled credits <c>0.41213·T</c>; the formula
    /// that circulates for this, <c>T·(√(1 + b·(1 − f/2)/B) − 1)</c>, says <c>0.41244·T</c>.
    /// </remarks>
    [TestMethod]
    public void TestUASingleAssetDepositMatchesTheNodesFigure()
    {
        decimal tokens = AmmMath.LPTokensForSingleAssetDeposit(
            poolBalance: 1_000_000m,
            deposit: 1_000_000m,
            lpTokenBalance: 1_000_000m,
            tradingFee: 1000);

        decimal asFractionOfT = tokens / 1_000_000m;

        Assert.AreEqual(
            0.41213m,
            Math.Round(asFractionOfT, 5),
            $"Equation 3 gives 0.41213·T for this pool; got {asFractionOfT}");
    }

    /// <summary>
    /// The approximation this replaces, shown to be wrong rather than asserted to be.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the two agree closely enough that a spot check does not tell them
    /// apart - 0.08% here - and because the error is always in the same direction: the
    /// approximation credits more tokens than the node will.
    /// </remarks>
    [TestMethod]
    public void TestUTheCirculatingApproximationIsWrongOnceThereIsAFee()
    {
        const decimal Pool = 1_000_000m;
        const uint Fee = 1000; // one per cent

        decimal exact = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, Fee) / Pool;

        // T * (sqrt(1 + b*(1 - f/2)/B) - 1), with b = B
        decimal f = AmmMath.TradingFeeFraction(Fee);
        decimal approximate = AmmMath.Sqrt(1m + (1m - f / 2m)) - 1m;

        Assert.AreEqual(0.41213m, Math.Round(exact, 5));
        Assert.AreEqual(0.41244m, Math.Round(approximate, 5));
        Assert.IsTrue(
            approximate > exact,
            "The approximation overstates the credit, which is the direction that disappoints a caller.");
    }

    /// <summary>
    /// Without a fee the two agree exactly, which is where the approximation came from.
    /// </summary>
    /// <remarks>
    /// This is what makes the difference easy to miss: the approximation is not a rough model of
    /// the wrong thing, it is the right formula with the fee handled loosely, so it is exact
    /// wherever there is no fee to handle.
    /// </remarks>
    [TestMethod]
    public void TestUWithoutAFeeTheTwoAgree()
    {
        const decimal Pool = 1_000_000m;

        decimal exact = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, tradingFee: 0) / Pool;
        decimal approximate = AmmMath.Sqrt(2m) - 1m;

        Assert.AreEqual(Math.Round(approximate, 20), Math.Round(exact, 20));
    }

    /// <summary>
    /// The auction slot holder trades at a tenth of the pool's fee, and is credited accordingly.
    /// </summary>
    /// <remarks>
    /// One of the two things the report names as making an otherwise correct estimate miss: the
    /// node computes the slot holder's deposits and withdrawals at <c>DiscountedFee</c>, so an
    /// estimate at the pool's fee is wrong for exactly the account most likely to be doing the
    /// estimating.
    /// </remarks>
    [TestMethod]
    public void TestUTheAuctionSlotHolderIsCreditedAtTheDiscountedFee()
    {
        const decimal Pool = 1_000_000m;

        Assert.AreEqual(100u, AmmMath.DiscountedTradingFee(1000), "A tenth of the pool's fee.");

        decimal atPoolFee = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, 1000);
        decimal atSlotFee = AmmMath.LPTokensForSingleAssetDeposit(Pool, Pool, Pool, AmmMath.DiscountedTradingFee(1000));

        Assert.IsTrue(
            atSlotFee > atPoolFee,
            "A smaller fee means more tokens for the same deposit; estimating at the pool's fee shortchanges the slot holder.");
    }

    /// <summary>
    /// A withdrawal costs at least what the same deposit earned, and more once there is a fee.
    /// </summary>
    /// <remarks>
    /// Equations 3 and 7 are separate formulas, and a transcription error in either would most
    /// likely show up as this invariant breaking - putting an asset in and taking the same amount
    /// straight back out cannot be free, or the pool could be drained by doing it repeatedly.
    /// </remarks>
    [TestMethod]
    public void TestUARoundTripCostsTheFee()
    {
        const decimal Pool = 1_000_000m;
        const decimal Amount = 100_000m;
        const uint Fee = 1000;

        decimal earned = AmmMath.LPTokensForSingleAssetDeposit(Pool, Amount, Pool, Fee);
        decimal spent = AmmMath.LPTokensForSingleAssetWithdraw(Pool + Amount, Amount, Pool + earned, Fee);

        Assert.IsTrue(
            spent > earned,
            $"Depositing and withdrawing the same amount must cost the fee, but earned {earned} and spent {spent}.");
    }

    /// <summary>
    /// Without a fee the round trip is exactly neutral, which is what pins equation 7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deposit <c>b</c> into a pool of <c>B</c>, then take the same <c>b</c> straight back out of
    /// the pool it has become. With no fee to pay, that must return precisely the tokens it earned
    /// - the pool ends where it started, so the LP must too.
    /// </para>
    /// <para>
    /// This is here because a mutation survived without it. Equation 7 multiplies by the fee where
    /// equation 3 multiplies by <c>1 − fee</c> - rippled's <c>lpTokensIn</c> calls <c>getFee</c>
    /// where <c>lpTokensOut</c> calls <c>feeMult</c> - and swapping the two passed every other test
    /// here, including the round-trip inequality, which is too loose to notice. The identity below
    /// is not: with the wrong multiplier the two sides stop matching by a wide margin.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TestUWithoutAFeeTheRoundTripIsExactlyNeutral()
    {
        const decimal Pool = 1_000_000m;
        const decimal Amount = 250_000m;
        const decimal Tokens = 1_000_000m;

        decimal earned = AmmMath.LPTokensForSingleAssetDeposit(Pool, Amount, Tokens, tradingFee: 0);
        decimal spent = AmmMath.LPTokensForSingleAssetWithdraw(
            Pool + Amount,
            Amount,
            Tokens + earned,
            tradingFee: 0);

        Assert.AreEqual(
            Math.Round(earned, 18),
            Math.Round(spent, 18),
            $"With no fee the two equations must invert each other exactly; earned {earned}, spent {spent}.");
    }

    [TestMethod]
    public void TestUAProportionalDepositIsLimitedByWhicheverAssetRunsOutFirst()
    {
        decimal tokens = AmmMath.LPTokensForProportionalDeposit(
            poolBalance1: 1_000m,
            poolBalance2: 4_000m,
            deposit1: 100m,      // a tenth of the pool
            deposit2: 200m,      // a twentieth, and therefore the limit
            lpTokenBalance: 2_000m);

        Assert.AreEqual(100m, tokens, "frac is the smaller of the two ratios: 200/4000 = 0.05.");

        (decimal asset1, decimal asset2) = AmmMath.AssetsForProportionalDeposit(1_000m, 4_000m, 100m, 200m);

        Assert.AreEqual(50m, asset1, "Only half of what was offered on the first asset is taken.");
        Assert.AreEqual(200m, asset2, "All of the limiting one.");
    }

    [TestMethod]
    public void TestUAProportionalWithdrawReturnsBothSidesAtTheSameFraction()
    {
        (decimal asset1, decimal asset2) = AmmMath.AssetsForProportionalWithdraw(
            poolBalance1: 1_000m,
            poolBalance2: 4_000m,
            lpTokens: 500m,
            lpTokenBalance: 2_000m);

        Assert.AreEqual(250m, asset1);
        Assert.AreEqual(1_000m, asset2);
    }

    /// <summary>
    /// The square root keeps more digits than a double one would.
    /// </summary>
    /// <remarks>
    /// The reason the class does its own: <see cref="Math.Sqrt"/> carries 15 significant digits and
    /// the rest of the arithmetic carries 28, so using it would throw away precision at the one step
    /// where the formulas need it most.
    /// </remarks>
    [TestMethod]
    public void TestUTheSquareRootIsExactToDecimalPrecision()
    {
        decimal root = AmmMath.Sqrt(2m);

        Assert.IsTrue(
            Math.Abs(root * root - 2m) < 0.0000000000000000000000001m,
            $"√2 squared came back as {root * root}");

        double viaDouble = Math.Sqrt(2.0);
        Assert.IsTrue(
            Math.Abs(root - (decimal)viaDouble) > 0m,
            "If this matched the double result exactly there would be no point computing it separately.");
    }

    [TestMethod]
    public void TestUImpossibleInputsAreRefusedRatherThanReturningNonsense()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(0m, 10m, 100m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(100m, -1m, 100m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetWithdraw(100m, 101m, 100m, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.AssetsForProportionalWithdraw(100m, 100m, 101m, 100m));
    }

    /// <summary>
    /// A fee in the wrong units is refused rather than quietly answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TradingFee</c> is in units of 1/100 000, and rippled caps it at 1000 - one per cent - in
    /// <c>kTradingFeeThreshold</c>. A caller who reaches for basis points or for whole per cent is
    /// out by a factor of ten or a hundred, and nothing in the arithmetic notices: at 5000 every
    /// intermediate value stays finite and a plausible, wrong number comes back. Only at 100 000
    /// does <c>1 - fee</c> reach zero and the division fail, and a <c>DivideByZeroException</c> is
    /// not what a caller should have to diagnose from.
    /// </para>
    /// <para>
    /// The bound is on the fee itself, so it holds even for an amount of zero - otherwise whether
    /// bad input is reported would depend on how much was being deposited.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TestUAFeeInTheWrongUnitsIsRefused()
    {
        Assert.AreEqual(1000u, AmmMath.TradingFeeThreshold);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(1_000m, 100m, 1_000m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetWithdraw(1_000m, 100m, 1_000m, tradingFee: 5000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.TradingFeeFraction(100_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.DiscountedTradingFee(1001));

        // Not conditional on there being an amount to compute.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => AmmMath.LPTokensForSingleAssetDeposit(1_000m, 0m, 1_000m, tradingFee: 5000));

        // And the cap itself is allowed - an off-by-one here would refuse the highest legal pool.
        AmmMath.LPTokensForSingleAssetDeposit(1_000m, 100m, 1_000m, AmmMath.TradingFeeThreshold);
    }

    [TestMethod]
    public void TestUDepositingNothingEarnsNothing()
    {
        Assert.AreEqual(0m, AmmMath.LPTokensForSingleAssetDeposit(100m, 0m, 100m, 1000));
        Assert.AreEqual(0m, AmmMath.LPTokensForSingleAssetWithdraw(100m, 0m, 100m, 1000));
    }
}
