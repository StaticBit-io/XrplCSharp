using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

[TestClass]
public class TestUAmmOfferMath
{
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };
    private static readonly IssuedCurrency Xts = new IssuedCurrency { Currency = "XTS", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };
    private static readonly IssuedCurrency Xxx = new IssuedCurrency { Currency = "XXX", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };

    private static XrplAmount Drops(long drops) => XrplAmount.Parse(Xrp, drops.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static XrplAmount Amount(IssuedCurrency asset, string value) => XrplAmount.Parse(asset, value);

    /// <summary><c>Offer_test.cpp</c>'s <c>testTickSize</c>: a gateway's tick size of 5 on the four offers it places.</summary>
    [TestMethod]
    public void RoundToTickSize_MatchesRippledsOfferTest()
    {
        (XrplAmount TakerPays, XrplAmount TakerGets) first = OfferCreateCrossing.RoundToTickSize(Amount(Xts, "10"), Amount(Xxx, "30"), 5, sell: false)!.Value;
        Assert.AreEqual(Amount(Xts, "10"), first.TakerPays);
        Assert.IsTrue(first.TakerGets < Amount(Xxx, "30") && first.TakerGets > Amount(Xxx, "29.9994"), first.TakerGets.ToString());

        (XrplAmount TakerPays, XrplAmount TakerGets) second = OfferCreateCrossing.RoundToTickSize(Amount(Xts, "30"), Amount(Xxx, "10"), 5, sell: false)!.Value;
        Assert.AreEqual(Amount(Xts, "30"), second.TakerPays);
        Assert.AreEqual(Amount(Xxx, "10"), second.TakerGets);

        (XrplAmount TakerPays, XrplAmount TakerGets) third = OfferCreateCrossing.RoundToTickSize(Amount(Xts, "10"), Amount(Xxx, "30"), 5, sell: true)!.Value;
        Assert.AreEqual(Amount(Xts, "10.0002"), third.TakerPays);
        Assert.AreEqual(Amount(Xxx, "30"), third.TakerGets);

        (XrplAmount TakerPays, XrplAmount TakerGets) fourth = OfferCreateCrossing.RoundToTickSize(Amount(Xts, "30"), Amount(Xxx, "10"), 5, sell: true)!.Value;
        Assert.AreEqual(Amount(Xts, "30"), fourth.TakerPays);
        Assert.AreEqual(Amount(Xxx, "10"), fourth.TakerGets);
    }

    [TestMethod]
    public void RoundToTickSize_WithoutATickSizeLeavesTheOffer()
    {
        XrplAmount pays = Amount(Xts, "10");
        XrplAmount gets = Amount(Xxx, "30");
        Assert.AreEqual((pays, gets), OfferCreateCrossing.RoundToTickSize(pays, gets, 0, sell: false));
        Assert.AreEqual((pays, gets), OfferCreateCrossing.RoundToTickSize(pays, gets, 16, sell: false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OfferCreateCrossing.RoundToTickSize(pays, gets, 17, sell: false));
    }

    [TestMethod]
    public void QualityRound_KeepsTheDigitsAndRoundsToAWorseQuality()
    {
        XrplQuality third = XrplQuality.FromAmounts(Amount(Xts, "10"), Amount(Xxx, "30"));
        XrplQuality rounded = third.Round(5);

        Assert.IsTrue(rounded <= third, "rounding up the rate gives a worse quality");
        Assert.AreEqual(XrplQuality.FromAmounts(Amount(Xts, "0.33334"), Amount(Xxx, "1")), rounded);
        Assert.AreEqual(third, third.Round(16));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => third.Round(0));
    }

    [TestMethod]
    public void PoolOffer_WithoutABookIsTheLargestOfferAtTheSpotPrice()
    {
        XrplAmount poolIn = Drops(10_000_000_000);
        XrplAmount poolOut = Amount(Usd, "10000");

        AmmPoolOffer offer = AmmOfferMath.PoolOffer(poolIn, poolOut, 1000, bookQuality: null)!.Value;

        Assert.AreEqual(Amount(Usd, "9900"), offer.Out);
        Assert.AreEqual(AmmOfferMath.SwapOut(poolIn, poolOut, offer.Out, 1000), offer.In);
        Assert.AreEqual(XrplQuality.FromAmounts(poolIn, poolOut), offer.Quality);
    }

    [TestMethod]
    public void PoolOffer_BesideABookBringsTheSpotPriceToIt()
    {
        XrplAmount poolIn = Drops(10_000_000_000);
        XrplAmount poolOut = Amount(Usd, "10000");
        XrplQuality spot = XrplQuality.FromAmounts(poolIn, poolOut);

        // A book one percent worse than the pool: the pool offers until its price reaches the book's.
        XrplQuality book = XrplQuality.FromAmounts(Drops(1_010_000), Amount(Usd, "1"));
        AmmPoolOffer offer = AmmOfferMath.PoolOffer(poolIn, poolOut, 0, book)!.Value;
        Assert.AreEqual(AmmOfferMath.ChangeSpotPriceQuality(poolIn, poolOut, book, 0)!.Value, (offer.In, offer.Out));
        Assert.IsTrue(offer.Quality >= book);

        // A book at least as good as the pool: the pool offers nothing.
        Assert.IsNull(AmmOfferMath.PoolOffer(poolIn, poolOut, 0, spot));
        Assert.IsNull(AmmOfferMath.PoolOffer(poolIn, poolOut, 0, XrplQuality.FromAmounts(Drops(990_000), Amount(Usd, "1"))));
    }

    [TestMethod]
    public void TradingFeeAboveTheProtocolMaximumIsRefused()
    {
        XrplAmount poolIn = Drops(10_000_000_000);
        XrplAmount poolOut = Amount(Usd, "10000");
        XrplQuality book = XrplQuality.FromAmounts(Drops(1_010_000), Amount(Usd, "1"));

        // 20% passed where 1/100,000 units are meant.
        Assert.ThrowsExactly<ArgumentException>(() => AmmOfferMath.SwapIn(poolIn, poolOut, Drops(1_000_000), 20_000));
        Assert.ThrowsExactly<ArgumentException>(() => AmmOfferMath.SwapOut(poolIn, poolOut, Amount(Usd, "1"), 20_000));
        Assert.ThrowsExactly<ArgumentException>(() => AmmOfferMath.ChangeSpotPriceQuality(poolIn, poolOut, book, 20_000));
        Assert.ThrowsExactly<ArgumentException>(() => AmmOfferMath.PoolOffer(poolIn, poolOut, 20_000, book));
        Assert.IsNotNull(AmmOfferMath.PoolOffer(poolIn, poolOut, AmmOfferMath.MaxTradingFee, bookQuality: null), "1% is allowed");

        // So is a snapshot built by hand with one.
        DexSnapshot snapshot = new DexSnapshot
        {
            Pools = new[]
            {
                new DexAmmPool { Account = "rPool", Balance = poolIn, Balance2 = poolOut, TradingFee = 20_000 },
            },
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            OfferCreateCrossing.Cross(snapshot, "rTaker", Amount(Usd, "1"), Drops(2_000_000), 10));
    }

    [TestMethod]
    public void Swaps_GoBothWaysAtThePoolsRounding()
    {
        XrplAmount poolIn = Drops(10_000_000_000);
        XrplAmount poolOut = Amount(Usd, "10000");

        XrplAmount received = AmmOfferMath.SwapIn(poolIn, poolOut, Drops(100_000_000), 1000);
        Assert.IsTrue(received > Amount(Usd, "0") && received < Amount(Usd, "100"), received.ToString());

        // Both round in the pool's favour - the output down, the input up - so the way back moves
        // only the last drop.
        XrplAmount cost = AmmOfferMath.SwapOut(poolIn, poolOut, received, 1000);
        Assert.IsTrue(cost >= Drops(99_999_999) && cost <= Drops(100_000_001), cost.ToString());
    }
}
