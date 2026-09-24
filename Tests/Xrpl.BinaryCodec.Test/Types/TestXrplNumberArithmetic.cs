using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;

namespace XrplTests.BinaryCodecLib.Types;

/// <summary>
/// The public arithmetic on <see cref="XrplNumber"/>. Every expected value was printed by rippled's
/// <c>Number.cpp</c> (commit 00606bec1, the same as <c>Fixtures/Number</c>) under the same scale and
/// rounding mode; <see cref="TestNumberVectors"/> covers the engine itself.
/// </summary>
[TestClass]
public class TestXrplNumberArithmetic
{
    private static XrplNumber N(string text) => XrplNumber.Parse(text);

    private static NumberContext Large330(NumberRounding rounding) => NumberContext.Create(NumberMantissaScale.Large330, rounding);

    [TestMethod]
    public void TestOperators_UseTheDefaultContext()
    {
        Assert.AreEqual("0.3333333333333333333", (N("1") / N("3")).ToString());
        Assert.AreEqual("0.6666666666666666667", (N("2") / N("3")).ToString());
        Assert.AreEqual("0.3", (N("0.1") + N("0.2")).ToString());
        Assert.AreEqual("-0.1", (N("0.1") - N("0.2")).ToString());
        Assert.AreEqual("0.02", (N("0.1") * N("0.2")).ToString());
        Assert.AreEqual(NumberMantissaScale.Large330, NumberContext.Default.Scale);
        Assert.AreEqual(NumberRounding.ToNearest, NumberContext.Default.Rounding);
    }

    [TestMethod]
    [DataRow(NumberRounding.ToNearest, "0.6666666666666666667", "-0.6666666666666666667")]
    [DataRow(NumberRounding.Upward, "0.6666666666666666667", "-0.6666666666666666666")]
    [DataRow(NumberRounding.Downward, "0.6666666666666666666", "-0.6666666666666666667")]
    [DataRow(NumberRounding.TowardsZero, "0.6666666666666666666", "-0.6666666666666666666")]
    public void TestDivide_RoundsInTheContextMode(NumberRounding rounding, string positive, string negative)
    {
        NumberContext context = Large330(rounding);
        Assert.AreEqual(positive, XrplNumber.Divide(N("2"), N("3"), context).ToString());
        Assert.AreEqual(negative, XrplNumber.Divide(N("-2"), N("3"), context).ToString());
    }

    [TestMethod]
    public void TestSmallScale_KeepsSixteenDigits()
    {
        NumberContext small = NumberContext.Create(NumberMantissaScale.Small);
        Assert.AreEqual("0.6666666666666667", XrplNumber.Divide(N("2"), N("3"), small).ToString());
        Assert.AreEqual("0.3333333333333333", XrplNumber.Divide(N("1"), N("3"), small).ToString());
    }

    [TestMethod]
    public void TestCusp_DependsOnTheScale()
    {
        XrplNumber max = (XrplNumber)long.MaxValue;
        XrplNumber legacy = XrplNumber.Add(max, 1, NumberContext.Create(NumberMantissaScale.LargeLegacy));
        XrplNumber cleanup330 = XrplNumber.Add(max, 1, NumberContext.Default);

        Assert.AreEqual(922337203685477581L, legacy.Mantissa);
        Assert.AreEqual(1, legacy.Exponent);
        Assert.AreEqual(long.MaxValue, cleanup330.Mantissa);
        Assert.AreEqual(0, cleanup330.Exponent);
    }

    [TestMethod]
    public void TestPowerAndRoots()
    {
        NumberContext context = NumberContext.Default;
        Assert.AreEqual("1.79585632602212915", XrplNumber.Power(N("1.05"), 12, context).ToString());
        Assert.AreEqual("1.414213562373095049", XrplNumber.Root2(N("2"), context).ToString());
        Assert.AreEqual("4", XrplNumber.Power(N("8"), 2, 3, context).ToString());
        Assert.AreEqual("2", XrplNumber.Root(N("8"), 3, context).ToString());
    }

    [TestMethod]
    public void TestIntegerConversions()
    {
        NumberContext nearest = NumberContext.Default;
        Assert.AreEqual(2L, N("2.5").ToInt64(nearest));
        Assert.AreEqual(4L, N("3.5").ToInt64(nearest));
        Assert.AreEqual(3L, N("2.1").ToInt64(Large330(NumberRounding.Upward)));
        Assert.AreEqual("-2", N("-2.75").Truncate(nearest).ToString());
    }

    [TestMethod]
    public void TestNegationAndAbs()
    {
        Assert.AreEqual("-1.5", (-N("1.5")).ToString());
        Assert.AreEqual("1.5", XrplNumber.Abs(N("-1.5")).ToString());
        Assert.AreEqual(XrplNumber.Zero, -XrplNumber.Zero);
    }

    [TestMethod]
    public void TestErrors()
    {
        NumberContext context = NumberContext.Default;
        Assert.ThrowsExactly<DivideByZeroException>(() => XrplNumber.Divide(N("1"), XrplNumber.Zero, context));
        Assert.ThrowsExactly<OverflowException>(() => XrplNumber.Root2(N("-4"), context));
        Assert.ThrowsExactly<OverflowException>(() => XrplNumber.Multiply(N("1e32000"), N("1e32000"), context));
        Assert.ThrowsExactly<ArgumentNullException>(() => XrplNumber.Add(N("1"), N("2"), null));
        Assert.ThrowsExactly<ArgumentNullException>(() => N("1").ToInt64(null));
    }

    [TestMethod]
    public void TestRoot_ThatRippledNeverReturnsFrom_Throws()
    {
        // rippled's Newton-Raphson loop for this input cycles with a period longer than two and
        // never ends (checked against Number.cpp with a timeout); the port reports it instead.
        NumberContext context = NumberContext.Create(NumberMantissaScale.Large320);
        XrplNumber x = XrplNumber.Parse("0.09223372036854775578");

        ArithmeticException error = Assert.ThrowsExactly<ArithmeticException>(() => XrplNumber.Root(x, 4, context));
        StringAssert.Contains(error.Message, "does not converge");
    }

    [TestMethod]
    [DataRow(false, false, false, NumberMantissaScale.Small)]
    [DataRow(true, false, false, NumberMantissaScale.LargeLegacy)]
    [DataRow(true, true, false, NumberMantissaScale.Large320)]
    [DataRow(true, true, true, NumberMantissaScale.Large330)]
    [DataRow(true, false, true, NumberMantissaScale.Large330)]
    [DataRow(false, true, true, NumberMantissaScale.Small)]
    public void TestForAmendments_SelectsTheScale(bool largeNumbers, bool fix320, bool fix330, NumberMantissaScale expected)
    {
        NumberContext context = NumberContext.ForAmendments(largeNumbers, fix320, fix330);
        Assert.AreEqual(expected, context.Scale);
        Assert.AreEqual(NumberRounding.ToNearest, context.Rounding);
        Assert.AreEqual(NumberRounding.Upward, context.WithRounding(NumberRounding.Upward).Rounding);
        Assert.AreEqual(expected, context.WithRounding(NumberRounding.Upward).Scale);
    }
}
