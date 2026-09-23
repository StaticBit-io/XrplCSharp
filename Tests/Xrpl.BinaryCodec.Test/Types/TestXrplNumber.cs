using System;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;

namespace XrplTests.BinaryCodecLib.Types;

/// <summary>
/// Vectors for <see cref="XrplNumber"/>. The text forms come from rippled's
/// <c>src/tests/libxrpl/basics/Number.cpp</c> (<c>NumberTest.to_string</c>, large mantissa scale) and
/// xrpl.js <c>ripple-binary-codec/test/st-number.test.ts</c>; the wire forms follow
/// <c>Number::mantissa()</c> / <c>Number::exponent()</c>.
/// </summary>
[TestClass]
public class TestXrplNumber
{
    [DataTestMethod]
    [DataRow(-2L, 0, "-2")]
    [DataRow(0L, 0, "0")]
    [DataRow(2L, 0, "2")]
    [DataRow(25L, -3, "0.025")]
    [DataRow(-25L, -3, "-0.025")]
    [DataRow(25L, 1, "250")]
    [DataRow(-25L, 1, "-250")]
    [DataRow(2L, 20, "2e20")]
    [DataRow(-2L, -20, "-2e-20")]
    [DataRow(2L, -10, "0.0000000002")]
    [DataRow(2L, -11, "2e-11")]
    [DataRow(-2L, 10, "-20000000000")]
    [DataRow(-2L, 11, "-2e11")]
    [DataRow(long.MaxValue, 0, "9223372036854775807")]
    [DataRow(-long.MaxValue, 0, "-9223372036854775807")]
    [DataRow(1L, -32750, "1e-32750")]
    [DataRow(long.MaxValue, 32768, "9223372036854775807e32768")]
    [DataRow(-long.MaxValue, 32768, "-9223372036854775807e32768")]
    public void ToString_MatchesRippled(long mantissa, int exponent, string expected)
    {
        XrplNumber number = new XrplNumber(mantissa, exponent);
        Assert.AreEqual(expected, number.ToString());
    }

    [DataTestMethod]
    [DataRow("0", "0")]
    [DataRow("-0", "0")]
    [DataRow("123.456", "123.456")]
    [DataRow("1.23e5", "123000")]
    [DataRow("-1.2e2", "-120")]
    [DataRow("-0.000000456", "-0.000000456")]
    [DataRow("0.002500", "0.0025")]
    [DataRow("10000000000", "10000000000")]
    [DataRow("100000000000", "1e11")]
    [DataRow("-100000000000", "-1e11")]
    [DataRow("0.00000000001", "1e-11")]
    [DataRow("9900000000000000000000", "99e20")]
    [DataRow("0.0000000000000000000099", "99e-22")]
    [DataRow("9999999999999999990", "9999999999999999990")]
    [DataRow("9223372036854775810", "9223372036854775810")]
    [DataRow("9223372036854775810e32768", "9223372036854775810e32768")]
    [DataRow("+007.50", "7.5")]
    [DataRow("5.", "5")]
    [DataRow(".5", "0.5")]
    [DataRow(" 42 ", "42")]
    [DataRow("1E+3", "1000")]
    public void Parse_ThenToString(string input, string expected)
    {
        Assert.AreEqual(expected, XrplNumber.Parse(input).ToString());
    }

    [DataTestMethod]
    [DataRow("9223372036854775895")]
    [DataRow("9323372036854775804")]
    [DataRow("92233720368547758079")]
    [DataRow("1.2345678901234567891")]
    [DataRow("12345678901234567890123")]
    public void Parse_RejectsValueThatCannotBeRepresentedExactly(string input)
    {
        FormatException error = Assert.ThrowsExactly<FormatException>(() => XrplNumber.Parse(input));
        StringAssert.Contains(error.Message, "exactly");
        Assert.IsFalse(XrplNumber.TryParse(input, out _));
    }

    [DataTestMethod]
    [DataRow("1e40000")]
    [DataRow("9223372036854775807e32769")]
    [DataRow("1e-40000")]
    [DataRow("1e-32751")]
    public void Parse_RejectsValueOutOfRange(string input)
    {
        Assert.ThrowsExactly<FormatException>(() => XrplNumber.Parse(input));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc123")]
    [DataRow("1.2.3")]
    [DataRow("1e")]
    [DataRow("1e+")]
    [DataRow(".")]
    [DataRow("-")]
    [DataRow("1 2")]
    [DataRow("0x10")]
    [DataRow("1e9999999999")]
    public void Parse_RejectsMalformedText(string input)
    {
        Assert.ThrowsExactly<FormatException>(() => XrplNumber.Parse(input));
        Assert.IsFalse(XrplNumber.TryParse(input, out XrplNumber value));
        Assert.AreEqual(XrplNumber.Zero, value);
    }

    [DataTestMethod]
    [DataRow("100", 1_000_000_000_000_000_000L, -16)]
    [DataRow("10000000000000", 1_000_000_000_000_000_000L, -5)]
    [DataRow("1", 1_000_000_000_000_000_000L, -18)]
    [DataRow("-42", -4_200_000_000_000_000_000L, -17)]
    [DataRow("9.5", 950_000_000_000_000_000L, -17)]
    [DataRow("9223372036854775807", long.MaxValue, 0)]
    [DataRow("9223372036854775810", 922_337_203_685_477_581L, 1)]
    [DataRow("9999999999999999990", 999_999_999_999_999_999L, 1)]
    public void WireForm_MatchesRippledExternalView(string input, long mantissa, int exponent)
    {
        XrplNumber number = XrplNumber.Parse(input);
        Assert.AreEqual(mantissa, number.Mantissa);
        Assert.AreEqual(exponent, number.Exponent);
        Assert.AreEqual(number, new XrplNumber(number.Mantissa, number.Exponent));
    }

    [TestMethod]
    public void Zero_HasRippledWireForm()
    {
        Assert.AreEqual(0L, XrplNumber.Zero.Mantissa);
        Assert.AreEqual(int.MinValue, XrplNumber.Zero.Exponent);
        Assert.IsTrue(XrplNumber.Zero.IsZero);
        Assert.AreEqual(0, XrplNumber.Zero.Sign);
        Assert.AreEqual(XrplNumber.Zero, default(XrplNumber));
        Assert.AreEqual(XrplNumber.Zero, new XrplNumber(0, 0));
        Assert.AreEqual(XrplNumber.Zero, new XrplNumber(0, int.MinValue));
        Assert.AreEqual(XrplNumber.Zero.GetHashCode(), new XrplNumber(0, 5).GetHashCode());
    }

    [TestMethod]
    public void Constructor_NormalizesNonCanonicalWireForm()
    {
        XrplNumber number = new XrplNumber(5, 0);
        Assert.AreEqual(5_000_000_000_000_000_000L, number.Mantissa);
        Assert.AreEqual(-18, number.Exponent);
        Assert.AreEqual(XrplNumber.Parse("5"), number);
    }

    [TestMethod]
    public void Constructor_UnderflowBecomesZero_AsInRippled()
    {
        Assert.AreEqual(XrplNumber.Zero, new XrplNumber(1, XrplNumber.MinExponent));
        Assert.AreEqual(XrplNumber.Zero, new XrplNumber(-7, int.MinValue + 1));
    }

    [TestMethod]
    public void Constructor_RejectsOverflowAndInt64MinValue()
    {
        Assert.ThrowsExactly<OverflowException>(() => new XrplNumber(1, 40000));
        Assert.ThrowsExactly<OverflowException>(() => new XrplNumber(long.MinValue, 0));
    }

    [TestMethod]
    public void Comparison_OrdersByValue()
    {
        XrplNumber[] ascending =
        {
            XrplNumber.Parse("-1e20"),
            XrplNumber.Parse("-1"),
            XrplNumber.Parse("-0.5"),
            XrplNumber.Zero,
            XrplNumber.Parse("1e-32750"),
            XrplNumber.Parse("0.5"),
            XrplNumber.Parse("1"),
            XrplNumber.Parse("9.5"),
            XrplNumber.Parse("10"),
            XrplNumber.Parse("9223372036854775807"),
            XrplNumber.Parse("9223372036854775810"),
            XrplNumber.Parse("1e20"),
        };

        for (int i = 0; i < ascending.Length; i++)
        {
            for (int j = 0; j < ascending.Length; j++)
            {
                int expected = i.CompareTo(j);
                Assert.AreEqual(expected, Math.Sign(ascending[i].CompareTo(ascending[j])), $"{ascending[i]} vs {ascending[j]}");
                Assert.AreEqual(i < j, ascending[i] < ascending[j]);
                Assert.AreEqual(i <= j, ascending[i] <= ascending[j]);
                Assert.AreEqual(i > j, ascending[i] > ascending[j]);
                Assert.AreEqual(i >= j, ascending[i] >= ascending[j]);
                Assert.AreEqual(i == j, ascending[i] == ascending[j]);
                Assert.AreEqual(i != j, ascending[i] != ascending[j]);
            }
        }
    }

    [TestMethod]
    public void Equality_IgnoresSpelling()
    {
        XrplNumber a = XrplNumber.Parse("1.50");
        XrplNumber b = XrplNumber.Parse("15e-1");
        Assert.AreEqual(a, b);
        Assert.IsTrue(a.Equals((object)b));
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreEqual(1, a.CompareTo(null));
        Assert.ThrowsExactly<ArgumentException>(() => a.CompareTo("1.5"));
    }

    [TestMethod]
    public void Sign_ReflectsValue()
    {
        Assert.AreEqual(-1, XrplNumber.Parse("-3").Sign);
        Assert.AreEqual(1, XrplNumber.Parse("3").Sign);
        Assert.IsTrue(XrplNumber.Parse("-3").IsNegative);
        Assert.IsFalse(XrplNumber.Zero.IsNegative);
    }

    [DataTestMethod]
    [DataRow("123.456", "123.456")]
    [DataRow("-0.000000456", "-0.000000456")]
    [DataRow("7.9e28", "79000000000000000000000000000")]
    [DataRow("9223372036854775807", "9223372036854775807")]
    [DataRow("1e-28", "0.0000000000000000000000000001")]
    [DataRow("1.5e-28", "0.0000000000000000000000000002")]
    [DataRow("2.5e-28", "0.0000000000000000000000000002")]
    [DataRow("-1.5e-28", "-0.0000000000000000000000000002")]
    [DataRow("1e-40", "0")]
    [DataRow("-1e-32750", "0")]
    [DataRow("0", "0")]
    public void TryToDecimal_ConvertsAndRoundsBeyondDecimalScale(string input, string expected)
    {
        Assert.IsTrue(XrplNumber.Parse(input).TryToDecimal(out decimal value));
        Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
        Assert.AreEqual(value, (decimal)XrplNumber.Parse(input));
    }

    [DataTestMethod]
    [DataRow("8e28")]
    [DataRow("-8e28")]
    [DataRow("1e29")]
    [DataRow("9223372036854775807e32768")]
    public void TryToDecimal_FailsOnOverflow(string input)
    {
        XrplNumber number = XrplNumber.Parse(input);
        Assert.IsFalse(number.TryToDecimal(out decimal value));
        Assert.AreEqual(0m, value);
        Assert.ThrowsExactly<OverflowException>(() => (decimal)number);
    }

    [TestMethod]
    public void FromDecimal_IsExactOrThrows()
    {
        Assert.AreEqual("123.456", ((XrplNumber)123.456m).ToString());
        Assert.AreEqual("1", ((XrplNumber)1.000m).ToString());
        Assert.AreEqual("-12345678901.23456789", ((XrplNumber)(-12345678901.23456789m)).ToString());
        Assert.IsTrue(((XrplNumber)0.000m).IsZero);
        Assert.ThrowsExactly<OverflowException>(() => (XrplNumber)0.1234567890123456789012345678m);
        Assert.ThrowsExactly<OverflowException>(() => (XrplNumber)decimal.MaxValue);
    }

    [TestMethod]
    public void FromIntegers()
    {
        XrplNumber fromInt = 5;
        Assert.AreEqual(XrplNumber.Parse("5"), fromInt);
        Assert.AreEqual("-2147483648", ((XrplNumber)int.MinValue).ToString());
        Assert.AreEqual("9223372036854775807", ((XrplNumber)long.MaxValue).ToString());
        Assert.ThrowsExactly<OverflowException>(() => (XrplNumber)long.MinValue);
    }

    private sealed class Holder
    {
        public XrplNumber Required { get; set; }

        public XrplNumber? Optional { get; set; }
    }

    [TestMethod]
    public void Json_WritesRippledTextAndReadsStringsOrNumbers()
    {
        Holder holder = new Holder { Required = XrplNumber.Parse("10000000000000"), Optional = null };
        Assert.AreEqual("{\"Required\":\"1e13\",\"Optional\":null}", JsonSerializer.Serialize(holder));

        Holder fromString = JsonSerializer.Deserialize<Holder>("{\"Required\":\"1e13\",\"Optional\":\"0.5\"}");
        Assert.AreEqual(XrplNumber.Parse("10000000000000"), fromString.Required);
        Assert.AreEqual(XrplNumber.Parse("0.5"), fromString.Optional);

        Holder fromNumber = JsonSerializer.Deserialize<Holder>("{\"Required\":10000000000000,\"Optional\":1.5e-3}");
        Assert.AreEqual(XrplNumber.Parse("10000000000000"), fromNumber.Required);
        Assert.AreEqual(XrplNumber.Parse("0.0015"), fromNumber.Optional);

        Holder absent = JsonSerializer.Deserialize<Holder>("{\"Required\":\"0\"}");
        Assert.IsNull(absent.Optional);
    }

    [DataTestMethod]
    [DataRow("{\"Required\":\"abc\"}")]
    [DataRow("{\"Required\":\"9223372036854775895\"}")]
    [DataRow("{\"Required\":true}")]
    public void Json_RejectsInvalidValue(string json)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Holder>(json));
    }
}
