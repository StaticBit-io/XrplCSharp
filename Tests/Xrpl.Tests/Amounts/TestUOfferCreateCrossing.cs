using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// <see cref="OfferCreateCrossing"/> against the offer crossings rippled's own
/// <c>AMM_test.cpp</c> (<c>testBasicPaymentEngine</c>) pins, rebuilt as snapshots: the pool's
/// balances, the offer left in the book and the taker's holdings must come out digit for digit,
/// under each set of amendments the test runs. Also the book walk's own rules on a plain book.
/// </summary>
[TestClass]
public class TestUOfferCreateCrossing
{
    private const string Gw = "gw";
    private const string Carol = "carol";
    private const string Bob = "bob";
    private const string Ed = "ed";
    private const string Pool = "amm";
    private const ulong Fee = 10;

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = Gw };
    private static readonly IssuedCurrency Gbp = new IssuedCurrency { Currency = "GBP", Issuer = Gw };
    private static readonly IssuedCurrency Eur = new IssuedCurrency { Currency = "EUR", Issuer = Gw };

    // AMM_test runs on every amendment except SingleAssetVault and LendingProtocol
    // (AMMTestBase::testableAmendments), so its figures are computed at the small Number scale,
    // with MPTokensV2 enabled.
    private static readonly LedgerRules AllRules = new LedgerRules { LargeNumbers = false, MPTokensV2 = true };
    private static readonly LedgerRules BeforeFixAmmV11 = new LedgerRules { LargeNumbers = false, MPTokensV2 = true, FixAMMv1_1 = false, FixAMMv1_3 = false };
    private static readonly LedgerRules WithoutReducedOffersV2 = new LedgerRules { LargeNumbers = false, MPTokensV2 = true, FixReducedOffersV2 = false };

    private static XrplAmount Drops(long drops) => XrplAmount.FromNumber(Xrp, (XrplNumber)drops);

    private static XrplAmount XrpOf(long xrp) => Drops(xrp * 1_000_000);

    private static XrplAmount Iou(IssuedCurrency asset, string value) => XrplAmount.Parse(asset, value);

    private static DexAccount Account(string address, long xrp = 30_000, uint ownerCount = 2, uint transferRate = 0) =>
        new DexAccount { Address = address, Balance = (ulong)(xrp * 1_000_000), OwnerCount = ownerCount, TransferRate = transferRate };

    private static DexTrustLine Line(string holder, IssuedCurrency asset, string balance) =>
        new DexTrustLine { Account = holder, Balance = Iou(asset, balance) };

    private static DexOffer Offer(string index, string owner, XrplAmount takerPays, XrplAmount takerGets) =>
        new DexOffer
        {
            Index = index,
            Account = owner,
            TakerPays = takerPays,
            TakerGets = takerGets,
            Quality = XrplQuality.FromAmounts(takerPays, takerGets),
        };

    private static DexAmmPool AmmPool(XrplAmount balance, XrplAmount balance2, ushort fee = 0) =>
        new DexAmmPool { Account = Pool, Balance = balance, Balance2 = balance2, TradingFee = fee };

    private static DexSnapshot Snapshot(
        IEnumerable<DexAccount> accounts,
        IEnumerable<DexTrustLine> lines,
        IEnumerable<DexOffer> offers = null,
        IEnumerable<DexAmmPool> pools = null) =>
        new DexSnapshot
        {
            ParentCloseTime = 800_000_000,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = accounts.ToList(),
            TrustLines = lines.ToList(),
            Offers = (offers ?? Array.Empty<DexOffer>()).ToList(),
            Pools = (pools ?? Array.Empty<DexAmmPool>()).ToList(),
        };

    private static XrplAmount Change(OfferCrossingResult result, string account, IssuedCurrency asset) =>
        result.BalanceChanges
            .Where(c => c.Account == account && XrplAmount.SameAsset(c.Change.Asset, asset))
            .Select(c => c.Change)
            .DefaultIfEmpty(XrplAmount.Zero(asset))
            .Single();

    private static void AssertPool(OfferCrossingResult result, XrplAmount balance, XrplAmount balance2)
    {
        Assert.HasCount(1, result.Pools, "the pool was traded against");
        Assert.AreEqual(balance, result.Pools[0].Balance, "the pool's first balance");
        Assert.AreEqual(balance2, result.Pools[0].Balance2, "the pool's second balance");
    }

    /// <summary>Offer crossing XRP/IOU: bob's offer takes 100 USD from a 10,000 XRP / 10,100 USD pool.</summary>
    [TestMethod]
    public void XrpForIou_AgainstThePool()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Bob, ownerCount: 1) },
            new[] { Line(Bob, Usd, "1000") },
            pools: new[] { AmmPool(XrpOf(10_000), Iou(Usd, "10100")) });

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Bob, Iou(Usd, "100"), XrpOf(100), Fee, rules: AllRules);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        AssertPool(result, XrpOf(10_100), Iou(Usd, "10000"));
        Assert.AreEqual(Iou(Usd, "100"), Change(result, Bob, Usd));
        Assert.AreEqual(-XrpOf(100), XrplAmountMath.Add(Change(result, Bob, Xrp), Drops((long)Fee)), "100 XRP and the fee");
        Assert.IsNull(result.PlacedTakerPays);
    }

    /// <summary>Offer crossing IOU/IOU with a 25% transfer fee: the bridge has no books, so one strand.</summary>
    [TestMethod]
    public void IouForIou_TransferRate_SinglePath()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw, transferRate: 1_250_000_000), Account(Carol) },
            new[] { Line(Carol, Gbp, "30000"), Line(Carol, Eur, "30000") },
            pools: new[] { AmmPool(Iou(Gbp, "1000"), Iou(Eur, "1100")) });

        foreach (LedgerRules rules in new[] { AllRules, BeforeFixAmmV11 })
        {
            OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Carol, Iou(Eur, "100"), Iou(Gbp, "100"), Fee, rules: rules);

            Assert.AreEqual("tesSUCCESS", result.EngineResult);
            AssertPool(result, Iou(Gbp, "1100"), Iou(Eur, "1000"));
            Assert.AreEqual(Iou(Gbp, "-125"), Change(result, Carol, Gbp), "100 GBP and the 25% fee");
            Assert.AreEqual(Iou(Eur, "100"), Change(result, Carol, Eur));
            Assert.IsNull(result.PlacedTakerPays);
        }
    }

    /// <summary>
    /// Single-path AMM offer with a 0.1% fee on what carol pays: before <c>fixAMMv1_1</c> the
    /// limit ignores the fee and nothing crosses; after it, the output is limited to the price.
    /// </summary>
    [TestMethod]
    public void SinglePathLimitOut_TransferFee()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw, transferRate: 1_001_000_000), Account(Carol) },
            new[] { Line(Carol, Usd, "30000") },
            pools: new[] { AmmPool(XrpOf(1_000), Iou(Usd, "500")) });

        OfferCrossingResult before = OfferCreateCrossing.Cross(snapshot, Carol, XrpOf(100), Iou(Usd, "55"), Fee, rules: BeforeFixAmmV11);
        Assert.IsEmpty(before.Pools, "before fixAMMv1_1 the pool is not crossed");
        Assert.AreEqual(XrpOf(100), before.PlacedTakerPays);
        Assert.AreEqual(Iou(Usd, "55"), before.PlacedTakerGets);

        // AMM offer ~50 USD / 91 XRP: the output is cut so the fee-inclusive quality meets the limit.
        OfferCrossingResult after = OfferCreateCrossing.Cross(snapshot, Carol, XrpOf(100), Iou(Usd, "55"), Fee, rules: AllRules);
        AssertPool(after, Drops(909_090_910), Iou(Usd, "549.99999945"));
        Assert.AreEqual(Drops(9_090_910), after.PlacedTakerPays);
        Assert.AreEqual(Iou(Usd, "5.0000005"), after.PlacedTakerGets);

        // Carol pays what reached the pool plus the 0.1% fee, rounded up. AMM_test states her
        // balance as 29949.95000060055, 5e-8 away from what its own pool figures give through
        // mulRatio; the same case on a live node (TestIBookCrossingAmm) agrees with this relation.
        XrplAmount poolIn = XrplAmountMath.Subtract(Iou(Usd, "549.99999945"), Iou(Usd, "500"));
        Assert.AreEqual(-OfferCrossing.MulRatio(poolIn, 1_001_000_000, OfferCrossing.QualityOne, roundUp: true), Change(after, Carol, Usd));
    }

    /// <summary>A small offer against the same pool, fully crossed, the rounding moved by <c>fixAMMv1_1</c>.</summary>
    [TestMethod]
    public void SinglePathSmallOffer_TransferFee()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw, transferRate: 1_001_000_000), Account(Carol) },
            new[] { Line(Carol, Usd, "30000") },
            pools: new[] { AmmPool(XrpOf(1_000), Iou(Usd, "500")) });

        OfferCrossingResult after = OfferCreateCrossing.Cross(snapshot, Carol, XrpOf(10), Iou(Usd, "5.5"), Fee, rules: AllRules);
        AssertPool(after, XrpOf(990), Iou(Usd, "505.0505050505051"));
        Assert.IsNull(after.PlacedTakerPays);

        OfferCrossingResult before = OfferCreateCrossing.Cross(snapshot, Carol, XrpOf(10), Iou(Usd, "5.5"), Fee, rules: BeforeFixAmmV11);
        AssertPool(before, XrpOf(990), Iou(Usd, "505.050505050505"));
        Assert.IsNull(before.PlacedTakerPays);
    }

    /// <summary>
    /// Multi-path AMM offer: a GBP/EUR pool with auto-bridge offers of worse quality. The pool
    /// offers Fibonacci slices while two strands run, the bridge follows, and the pool finishes on
    /// a single path - sized to the limit with the transfer fee after <c>fixAMMv1_1</c>, without it before.
    /// </summary>
    [TestMethod]
    public void MultiPathPoolAndAutoBridge_TransferRate()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw, transferRate: 1_250_000_000), Account(Carol), Account(Bob, ownerCount: 3), Account(Ed, ownerCount: 3) },
            new[]
            {
                Line(Carol, Gbp, "30000"), Line(Carol, Eur, "30000"),
                Line(Bob, Gbp, "2000"), Line(Bob, Eur, "2000"),
                Line(Ed, Gbp, "2000"), Line(Ed, Eur, "2000"),
            },
            new[]
            {
                Offer("B0B", Bob, Iou(Gbp, "10"), XrpOf(10)),
                Offer("ED0", Ed, XrpOf(10), Iou(Eur, "10")),
            },
            new[] { AmmPool(Iou(Gbp, "1000"), Iou(Eur, "1100")) });

        foreach (LedgerRules rules in new[] { AllRules, WithoutReducedOffersV2 })
        {
            OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Carol, Iou(Eur, "100"), Iou(Gbp, "100"), Fee, rules: rules);

            Assert.AreEqual("tesSUCCESS", result.EngineResult);
            AssertPool(result, Iou(Gbp, "1060.684828792832"), Iou(Eur, "1037.06583722134"));
            Assert.AreEqual(Iou(Eur, "27.06583722134028"), result.PlacedTakerPays);
            Assert.AreEqual(Iou(Gbp, "27.06583722134028"), result.PlacedTakerGets);
            Assert.AreEqual(Iou(Gbp, "29911.64396400896"), XrplAmountMath.Add(Iou(Gbp, "30000"), Change(result, Carol, Gbp)));
            Assert.AreEqual(Iou(Eur, "30072.93416277865"), XrplAmountMath.Add(Iou(Eur, "30000"), Change(result, Carol, Eur)));
            Assert.AreEqual(Iou(Gbp, "10"), Change(result, Bob, Gbp));
            Assert.AreEqual(Iou(Eur, "-12.5"), Change(result, Ed, Eur));
            CollectionAssert.AreEquivalent(new[] { "B0B", "ED0" }, result.Offers.Select(o => o.Index).ToArray(), "both bridge offers change");
            Assert.IsTrue(result.Offers.All(o => o.Deleted), "both bridge offers are taken");
        }

        OfferCrossingResult legacy = OfferCreateCrossing.Cross(snapshot, Carol, Iou(Eur, "100"), Iou(Gbp, "100"), Fee, rules: BeforeFixAmmV11);
        AssertPool(legacy, Iou(Gbp, "1037.06583722133"), Iou(Eur, "1060.684828792831"));
        Assert.AreEqual(Iou(Eur, "50.684828792831"), legacy.PlacedTakerPays);
        Assert.AreEqual(Iou(Gbp, "50.684828792831"), legacy.PlacedTakerGets);
        Assert.AreEqual(Iou(Gbp, "29941.16770347333"), XrplAmountMath.Add(Iou(Gbp, "30000"), Change(legacy, Carol, Gbp)));
        Assert.AreEqual(Iou(Eur, "30049.31517120716"), XrplAmountMath.Add(Iou(Eur, "30000"), Change(legacy, Carol, Eur)));
    }

    /// <summary>
    /// The book walk's removals: an expired offer and one whose owner holds nothing are
    /// removed; the next offer is crossed.
    /// </summary>
    [TestMethod]
    public void ExpiredAndUnfundedOffersAreRemoved()
    {
        DexOffer expired = Offer("E1", "m1", XrpOf(3), Iou(Usd, "10"));
        DexOffer expiredWithTime = new DexOffer
        {
            Index = expired.Index, Account = expired.Account, TakerPays = expired.TakerPays, TakerGets = expired.TakerGets,
            Quality = expired.Quality, Expiration = 800_000_000,
        };
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Carol, ownerCount: 1), Account("m1"), Account("m2"), Account("m3") },
            new[] { Line(Carol, Usd, "0"), Line("m1", Usd, "100"), Line("m2", Usd, "0"), Line("m3", Usd, "100") },
            new[]
            {
                expiredWithTime,
                Offer("U2", "m2", XrpOf(3), Iou(Usd, "10")),
                Offer("F3", "m3", XrpOf(4), Iou(Usd, "10")),
            });

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Carol, Iou(Usd, "10"), XrpOf(5), Fee);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(3, result.Offers.Count(o => o.Deleted), "expired, unfunded and taken");
        Assert.AreEqual(Iou(Usd, "10"), Change(result, Carol, Usd));
        Assert.AreEqual(Iou(Usd, "-10"), Change(result, "m3", Usd));
        Assert.IsNull(result.PlacedTakerPays);
    }

    /// <summary>An account with nothing to pay is refused before any crossing; fill-or-kill keeps only the removals.</summary>
    [TestMethod]
    public void UnfundedTakerAndFillOrKill()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Carol), Account("m1") },
            new[] { Line(Carol, Usd, "0"), Line("m1", Usd, "5") },
            new[] { Offer("F1", "m1", XrpOf(3), Iou(Usd, "10")) });

        OfferCrossingResult unfunded = OfferCreateCrossing.Cross(snapshot, Carol, XrpOf(1), Iou(Usd, "1"), Fee);
        Assert.AreEqual("tecUNFUNDED_OFFER", unfunded.EngineResult);

        OfferCrossingResult killed = OfferCreateCrossing.Cross(snapshot, Carol, Iou(Usd, "10"), XrpOf(3), Fee, OfferCreateFlags.tfFillOrKill);
        Assert.AreEqual("tecKILLED", killed.EngineResult);
        Assert.IsEmpty(killed.Offers, "the partly funded offer stays");
        Assert.AreEqual(Drops(-(long)Fee), Change(killed, Carol, Xrp), "only the fee");
    }

    [TestMethod]
    public void MptSidesAreNotSupported()
    {
        IssuedCurrency mpt = new IssuedCurrency { MptIssuanceId = "0000000000000000000000000000000000000000000000AA" };
        DexSnapshot snapshot = Snapshot(new[] { Account(Carol) }, Array.Empty<DexTrustLine>());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            OfferCreateCrossing.Cross(snapshot, Carol, XrplAmount.Parse(mpt, "10"), XrpOf(1), Fee));
    }

    /// <summary><c>testFixChangeSpotPriceQuality</c>: which pools can be brought to a quality, at the small <c>Number</c> scale.</summary>
    [TestMethod]
    public void ChangeSpotPriceQuality_MatchesRippledsTable()
    {
        LedgerRules fixedRules = new LedgerRules { LargeNumbers = false };
        LedgerRules legacyRules = new LedgerRules { LargeNumbers = false, FixAMMv1_1 = false, FixAMMv1_3 = false };
        IssuedCurrency iou = new IssuedCurrency { Currency = "IOU", Issuer = Gw };

        XrplAmount Pool(string value) => value.All(char.IsDigit) ? XrplAmount.Parse(Xrp, value) : XrplAmount.Parse(iou, value);

        int checkedCases = 0;
        foreach ((string poolIn, string poolOut, ulong qualityValue, ushort fee, string status) in ChangeSpotPriceQualityCases)
        {
            XrplAmount @in = Pool(poolIn);
            XrplAmount @out = Pool(poolOut);
            XrplQuality quality = new XrplQuality(qualityValue);

            (XrplAmount In, XrplAmount Out)? amounts = AmmSwap.ChangeSpotPriceQuality(@in, @out, quality, fee, fixedRules);
            string label = $"{poolIn}/{poolOut} q={qualityValue} fee={fee}";
            switch (status)
            {
                case "Succeed":
                case "FailShouldSucceed":
                case "SucceedShouldSucceedResize":
                    Assert.IsNotNull(amounts, $"{label}: generated after fixAMMv1_1");
                    Assert.IsTrue(XrplQuality.FromAmounts(amounts.Value.In, amounts.Value.Out, fixedRules) >= quality, $"{label}: at the quality");
                    break;
                default:
                    Assert.IsNull(amounts, $"{label}: not generated after fixAMMv1_1");
                    break;
            }

            if (status == "SucceedShouldFail")
            {
                (XrplAmount In, XrplAmount Out)? legacy = AmmSwap.ChangeSpotPriceQuality(@in, @out, quality, fee, legacyRules);
                Assert.IsNotNull(legacy, $"{label}: generated before fixAMMv1_1");
                XrplQuality offered = XrplQuality.FromAmounts(legacy.Value.In, legacy.Value.Out, legacyRules);
                Assert.IsTrue(offered < quality && XrplQuality.WithinRelativeDistance(offered, quality, new XrplNumber(1, -7), legacyRules), label);
            }

            checkedCases++;
        }

        Assert.AreEqual(ChangeSpotPriceQualityCases.Length, checkedCases);
        Assert.IsNull(AmmSwap.SolveQuadraticSmallest(1, 1, 1, fixedRules.Context), "a negative discriminant has no root");
    }

    /// <summary>The table of <c>AMM_test::testFixChangeSpotPriceQuality</c> with a quality given by its encoding.</summary>
    private static readonly (string PoolIn, string PoolOut, ulong Quality, ushort Fee, string Status)[] ChangeSpotPriceQualityCases =
    {
        ("0.001519763260828713", "1558701", 5414253689393440221, 1000, "FailShouldSucceed"),
        ("0.01099814367603737", "1892611", 5482264816516900274, 1000, "FailShouldSucceed"),
        ("0.78", "796599", 5630392334958379008, 1000, "FailShouldSucceed"),
        ("105439.2955578965", "49398693", 5910869983721805038, 400, "FailShouldSucceed"),
        ("12408293.23445213", "4340810521", 5911611095910090752, 997, "FailShouldSucceed"),
        ("1892611", "0.01099814367603737", 6703103457950430139, 1000, "FailShouldSucceed"),
        ("423028.8508101858", "3392804520", 5837920340654162816, 600, "FailShouldSucceed"),
        ("44565388.41001027", "73890647", 6058976634606450001, 1000, "FailShouldSucceed"),
        ("66831.68494832662", "16", 6346111134641742975, 0, "FailShouldSucceed"),
        ("675.9287302203422", "1242632304", 5625960929244093294, 300, "FailShouldSucceed"),
        ("7047.112186735699", "1649845866", 5696855348026306945, 504, "FailShouldSucceed"),
        ("840236.4402981238", "47419053", 5982561601648018688, 499, "FailShouldSucceed"),
        ("992715.618909774", "189445631733", 5697835648288106944, 815, "SucceedShouldSucceedResize"),
        ("504636667521", "185545883.9506651", 6343802275337659280, 503, "SucceedShouldSucceedResize"),
        ("992706.7218636649", "189447316000", 5697835648288106944, 797, "SucceedShouldSucceedResize"),
        ("1.068737911388205", "127860278877", 5268604356368739396, 293, "SucceedShouldSucceedResize"),
        ("17932506.56880419", "189308.6043676173", 6206460598195440068, 311, "SucceedShouldSucceedResize"),
        ("1.066379294658174", "128042251493", 5268559341368739328, 270, "SucceedShouldSucceedResize"),
        ("350131413924", "1576879.110907892", 6487411636539049449, 650, "Fail"),
        ("422093460", "2.731797662057464", 6702911108534394924, 1000, "Fail"),
        ("76128132223", "367172.7148422662", 6487263463413514240, 548, "Fail"),
        ("132701839250", "280703770.7695443", 6273750681188885075, 562, "Fail"),
        ("994165.7604612011", "189551302411", 5697835592690668727, 815, "Fail"),
        ("45053.33303227917", "86612695359", 5625695218943638190, 500, "Fail"),
        ("199649.077043865", "14017933007", 5766034667318524880, 324, "Fail"),
        ("27751824831.70903", "78896950", 6272538159621630432, 500, "Fail"),
        ("225.3731275781907", "156431793648", 5477818047604078924, 989, "Fail"),
        ("199649.077043865", "14017933007", 5766036094462806309, 324, "Fail"),
        ("3.590272027140361", "20677643641", 5406056147042156356, 808, "Fail"),
        ("1.070884664490231", "127604712776", 5268620608623825741, 293, "Fail"),
        ("3272.448829820197", "6275124076", 5625710328924117902, 81, "Fail"),
        ("0.009059512633902926", "7994028", 5477511954775533172, 1000, "Fail"),
        ("69864389131", "287631.4543025075", 6487623473313516078, 451, "Succeed"),
        ("4328342973", "12453825.99247381", 6272522264364865181, 997, "Succeed"),
        ("32347017", "7003.93031579449", 6347261126087916670, 1000, "Succeed"),
        ("61697206161", "36631.4583206413", 6558965195382476659, 500, "Succeed"),
        ("1654524979", "7028.659825511603", 6487551345110052981, 504, "Succeed"),
        ("88621.22277293179", "5128418948", 5766347291552869205, 380, "Succeed"),
        ("1892611", "0.01099814367603737", 6703102780512015436, 1000, "Succeed"),
        ("4542.639373338766", "24554809", 5838994982188783710, 0, "Succeed"),
        ("5132932546", "88542.99750172683", 6419203342950054537, 380, "Succeed"),
        ("78929964.1549083", "1506494795", 5986890029845558688, 589, "Succeed"),
        ("10096561906", "44727.72453735605", 6487455290284644551, 250, "Succeed"),
        ("5092.219565514988", "8768257694", 5626349534958379008, 503, "Succeed"),
        ("1819778294", "8305.084302902864", 6487429398998540860, 415, "Succeed"),
        ("6970462.633911943", "57359281", 6054087899185946624, 850, "Succeed"),
        ("3983448845", "2347.543644281467", 6558965195382476659, 856, "Succeed"),
        ("771493171", "1.243473020567508", 6707566798038544272, 100, "SucceedShouldFail"),
    };
}
