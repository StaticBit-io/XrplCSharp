using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// <see cref="XrplAmount"/>, <see cref="XrplQuality"/> and <see cref="OfferCrossing"/> against the
/// cases rippled's own tests pin (<c>Quality_test.cpp</c>, <c>STAmount_test.cpp</c>), and the
/// conversions to and from the wire model.
/// </summary>
[TestClass]
public class TestUXrplAmount
{
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq" };

    private static XrplAmount Drops(long drops) => XrplAmount.FromNumber(Xrp, (XrplNumber)drops);

    /// <summary><c>Quality_test::testCeilIn</c>: amounts in drops, the quality from (in, out).</summary>
    [TestMethod]
    [DataRow(1, 1, 1, 1, 1, 1, 1)]
    [DataRow(1, 1, 10, 10, 5, 5, 5)]
    [DataRow(1, 1, 5, 5, 10, 5, 5)]
    [DataRow(1, 2, 40, 80, 40, 40, 80)]
    [DataRow(1, 2, 40, 80, 20, 20, 40)]
    [DataRow(1, 2, 40, 80, 60, 40, 80)]
    [DataRow(2, 1, 40, 20, 20, 20, 10)]
    [DataRow(2, 1, 40, 20, 40, 40, 20)]
    [DataRow(2, 1, 40, 20, 50, 40, 20)]
    public void CeilIn_MatchesRippledsQualityTest(int qIn, int qOut, int @in, int @out, int limit, int expectedIn, int expectedOut)
    {
        XrplQuality quality = XrplQuality.FromAmounts(Drops(qIn), Drops(qOut));

        (XrplAmount resultIn, XrplAmount resultOut) = quality.CeilIn(Drops(@in), Drops(@out), Drops(limit));

        Assert.AreEqual(Drops(expectedIn), resultIn);
        Assert.AreEqual(Drops(expectedOut), resultOut);
    }

    /// <summary><c>Quality_test::testCeilOut</c>.</summary>
    [TestMethod]
    [DataRow(1, 1, 1, 1, 1, 1, 1)]
    [DataRow(1, 1, 10, 10, 5, 5, 5)]
    [DataRow(1, 1, 10, 10, 20, 10, 10)]
    [DataRow(1, 2, 40, 80, 40, 20, 40)]
    [DataRow(1, 2, 40, 80, 80, 40, 80)]
    [DataRow(1, 2, 40, 80, 100, 40, 80)]
    [DataRow(2, 1, 40, 20, 20, 40, 20)]
    [DataRow(2, 1, 40, 20, 40, 40, 20)]
    [DataRow(2, 1, 40, 20, 10, 20, 10)]
    public void CeilOut_MatchesRippledsQualityTest(int qIn, int qOut, int @in, int @out, int limit, int expectedIn, int expectedOut)
    {
        XrplQuality quality = XrplQuality.FromAmounts(Drops(qIn), Drops(qOut));

        (XrplAmount resultIn, XrplAmount resultOut) = quality.CeilOut(Drops(@in), Drops(@out), Drops(limit));

        Assert.AreEqual(Drops(expectedIn), resultIn);
        Assert.AreEqual(Drops(expectedOut), resultOut);
    }

    /// <summary><c>Quality_test::testRaw</c>: a limit that rounds the input to zero drops must not.</summary>
    [TestMethod]
    public void CeilOut_DoesNotRoundTheInputToZero()
    {
        XrplQuality quality = new XrplQuality(0x5d048191fb9130daUL);
        XrplAmount limit = XrplAmount.FromNumber(Usd, new XrplNumber(4131113916555555, -16));

        (XrplAmount resultIn, _) = quality.CeilOut(Drops(349469768), XrplAmount.FromNumber(Usd, new XrplNumber(2755280000000000, -15)), limit);

        Assert.IsFalse(resultIn.IsZero);
    }

    /// <summary><c>STAmount_test</c>: <c>getRate</c> of 1 and 10, in drops or as unitless values.</summary>
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void GetRate_MatchesRippledsStAmountTest(bool outIsXrp, bool inIsXrp)
    {
        XrplAmount One(bool xrp) => xrp ? Drops(1) : XrplAmount.FromNumber(Usd, 1);
        XrplAmount Ten(bool xrp) => xrp ? Drops(10) : XrplAmount.FromNumber(Usd, 10);

        // getRate(out, in) is the quality of an offer taking `in` for `out`.
        Assert.AreEqual(((100UL - 14) << 56) | 1_000_000_000_000_000UL, XrplQuality.FromAmounts(Ten(inIsXrp), One(outIsXrp)).Value);
        Assert.AreEqual(((100UL - 16) << 56) | 1_000_000_000_000_000UL, XrplQuality.FromAmounts(One(inIsXrp), Ten(outIsXrp)).Value);
    }

    [TestMethod]
    [DataRow("1", "1")]
    [DataRow("1.5", "1.5")]
    [DataRow("-0.000001", "-0.000001")]
    [DataRow("123456789012345678", "1234567890123457e2")]
    [DataRow("1e90", "1000000000000000e75")]
    [DataRow("-9999999999999999e80", "-9999999999999999e80")]
    [DataRow("1e-81", "1000000000000000e-96")]
    [DataRow("0.00000000000000000000000001", "1000000000000000e-41")]
    [DataRow("0", "0")]
    [DataRow("12345678901", "12345678901")]
    [DataRow("123456789012", "1234567890120000e-4")]
    [DataRow("-100000000000", "-1000000000000000e-4")]
    [DataRow("0.0000000001", "0.0000000001")]
    [DataRow("0.00000000001", "1000000000000000e-26")]
    public void IssuedCurrency_ReadsAndWritesAsRippledDoes(string text, string written)
    {
        XrplAmount amount = XrplAmount.Parse(Usd, text);

        Assert.AreEqual(written, amount.ToString());
        Assert.AreEqual(amount, XrplAmount.Parse(Usd, amount.ToString()));
    }

    /// <summary>The scientific form <c>getText</c> writes reads back through <see cref="Currency.ValueAsNumber"/>.</summary>
    [TestMethod]
    [DataRow("1234567890120000e-4", "123456789012")]
    [DataRow("-1000000000000000e-4", "-100000000000")]
    [DataRow("1000000000000000e-26", "0.00000000001")]
    public void ScientificValueReadsAsDecimal(string value, string expected)
    {
        Currency currency = new Currency { CurrencyCode = "USD", Issuer = Usd.Issuer, Value = value };
        Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), currency.ValueAsNumber);
    }

    [TestMethod]
    public void IssuedCurrency_BeyondTheExponentRange_Overflows()
    {
        Assert.ThrowsExactly<OverflowException>(() => XrplAmount.Parse(Usd, "1e97"));
        Assert.IsTrue(XrplAmount.Parse(Usd, "1e-97").IsZero, "below the range is zero, as rippled has it");
    }

    [TestMethod]
    public void Xrp_IsWholeDrops()
    {
        Assert.AreEqual("-25", XrplAmount.Parse(Xrp, "-25").ToString());
        Assert.ThrowsExactly<FormatException>(() => XrplAmount.Parse(Xrp, "1.5"));
        Assert.ThrowsExactly<OverflowException>(() => XrplAmount.Parse(Xrp, "100000000000000001"));
    }

    [TestMethod]
    public void Currency_ConvertsBothWays()
    {
        Currency wire = new Currency { CurrencyCode = "USD", Issuer = Usd.Issuer, Value = "9999999999999999e80" };

        XrplAmount amount = wire.ToXrplAmount();
        Currency back = amount.ToCurrency();

        Assert.AreEqual(AmountKind.Iou, amount.Kind);
        Assert.AreEqual("9999999999999999e80", back.Value);
        Assert.AreEqual("USD", back.CurrencyCode);
        Assert.AreEqual(Usd.Issuer, back.Issuer);
        Assert.AreEqual(AmountKind.Xrp, new Currency { Value = "15" }.ToXrplAmount().Kind);
    }

    [TestMethod]
    public void Offer_BookQuality_IsTheDirectorysLastSixteenHexDigits()
    {
        Offer offer = new Offer
        {
            BookDirectory = "DFA3B6DDAB58C7E8E5D944E736DA4B7046C30E4F460FD9DE4E1566BBCC7F3C3F",
            TakerGets = new Currency { Value = "1000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = Usd.Issuer, Value = "2" },
        };

        Assert.AreEqual(0x4E1566BBCC7F3C3FUL, offer.BookQuality.Value.Value);
        Assert.AreEqual(XrplQuality.FromAmounts(XrplAmount.FromNumber(Usd, 2), Drops(1_000_000)), offer.RemainingQuality);
    }

    [TestMethod]
    public void Quality_OrdersWorseBelowBetter()
    {
        XrplQuality cheap = XrplQuality.FromAmounts(Drops(1), Drops(3));
        XrplQuality dear = XrplQuality.FromAmounts(Drops(1), Drops(2));

        Assert.IsTrue(dear < cheap, "getting 2 for 1 is worse than getting 3 for 1");
        Assert.IsTrue(cheap.Value < dear.Value, "a better quality has the lower encoding");
    }

    [TestMethod]
    public void MulRound_IntoXrp_RoundsUpOnlyWhenAsked()
    {
        XrplAmount third = XrplAmount.FromNumber(Usd, XrplNumber.Parse("0.3333333333333333"));

        Assert.AreEqual(Drops(3), XrplAmountMath.MulRoundStrict(Drops(10), third, Xrp, roundUp: false));
        Assert.AreEqual(Drops(4), XrplAmountMath.MulRoundStrict(Drops(10), third, Xrp, roundUp: true));
    }

    [TestMethod]
    public void MulRatio_TransferFee_RoundsTheLastDropUp()
    {
        // 1001 drops at a 0.2% transfer rate is 1003.002 drops.
        Assert.AreEqual(Drops(1004), OfferCrossing.MulRatio(Drops(1001), 1_002_000_000, OfferCrossing.QualityOne, roundUp: true));
        Assert.AreEqual(Drops(1003), OfferCrossing.MulRatio(Drops(1001), 1_002_000_000, OfferCrossing.QualityOne, roundUp: false));
    }

    [TestMethod]
    public void Fund_OwnerWithLessThanTheOffer_IsCutToTheirFunds()
    {
        XrplQuality quality = XrplQuality.FromAmounts(Drops(100), Drops(50));

        OfferStep step = OfferCrossing.Fund(quality, Drops(100), Drops(50), ownerFunds: Drops(20));

        Assert.AreEqual(Drops(20), step.OwnerGives);
        Assert.AreEqual(Drops(20), step.StepOut);
        Assert.AreEqual(Drops(40), step.StepIn);
    }
}
