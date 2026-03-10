using System.Globalization;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Utils;

namespace XrplTests.Xrpl.Models;

[TestClass]
public class TestTokenPrecision
{
    [DataTestMethod]
    [DataRow(123.456789, true, 123.456789)]
    [DataRow(123.4567891, true, 123.456789)]
    [DataRow(0.123456789123456, false, 0.123456789123456)]
    [DataRow(1.9999999, true, 1.999999)]
    [DataRow(-123.456789, true, -123.456789)]
    public void TestRoundTokenAmount_Truncate(double valueD, bool isXrp, double expectedD)
    {
        decimal value = (decimal)valueD;
        decimal expected = (decimal)expectedD;
        var result = value.RoundTokenAmount(isXrp, PrecisionRoundingMode.Truncate);
        Assert.AreEqual(expected, result);
    }

    [DataTestMethod]
    [DataRow(123.4567894, true, 123.456789)]
    [DataRow(123.4567895, true, 123.45679)]
    [DataRow(0.1234567891234564, false, 0.123456789123456)]
    public void TestRoundTokenAmount_Round(double valueD, bool isXrp, double expectedD)
    {
        decimal value = (decimal)valueD;
        decimal expected = (decimal)expectedD;
        var result = value.RoundTokenAmount(isXrp, PrecisionRoundingMode.Round);
        Assert.AreEqual(expected, result);
    }

    [DataTestMethod]
    [DataRow("123.4500", true, "123.45")]
    [DataRow("100.000000", true, "100")]
    [DataRow("0.123456789123456", false, "0.123456789123456")]
    [DataRow("0.000000", true, "0")]
    public void TestFormatTokenAmount_Truncate(string valueStr, bool isXrp, string expected)
    {
        var v = decimal.Parse(valueStr, CultureInfo.InvariantCulture);
        var result = v.FormatTokenAmount(isXrp, PrecisionRoundingMode.Truncate);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void TestRoundTokenAmount_Truncate_HighPrecisionNonXrp()
    {
        decimal value = 0.1234567891234567m;
        decimal expected = 0.123456789123456m;
        var result = value.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void TestRoundTokenAmount_Round_HighPrecisionNonXrp()
    {
        decimal value = 0.1234567891234565m;
        decimal expected = 0.123456789123457m;
        var result = value.RoundTokenAmount(false, PrecisionRoundingMode.Round);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void TestRoundTokenAmount_Nullable_Null()
    {
        decimal? value = null;
        var result = value.RoundTokenAmount(true);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TestRoundTokenAmount_Nullable_HasValue()
    {
        decimal? value = 123.4567891m;
        var result = value.RoundTokenAmount(true);
        Assert.AreEqual(123.456789m, result);
    }

    [TestMethod]
    public void TestTruncateDecimals_ZeroDecimals()
    {
        var method = typeof(TokenPrecision).GetMethod(
            "TruncateDecimals",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);
        var result = (decimal)method.Invoke(null, new object[] { 123.987m, 0 });
        Assert.AreEqual(123m, result);
    }

    [TestMethod]
    public void TestPow10()
    {
        var method = typeof(TokenPrecision).GetMethod(
            "Pow10",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);
        Assert.AreEqual(1m, (decimal)method.Invoke(null, new object[] { 0 }));
        Assert.AreEqual(10m, (decimal)method.Invoke(null, new object[] { 1 }));
        Assert.AreEqual(100m, (decimal)method.Invoke(null, new object[] { 2 }));
    }
}
