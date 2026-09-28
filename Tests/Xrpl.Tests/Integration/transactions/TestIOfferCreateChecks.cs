using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="OfferCreateCrossing"/> run on the <c>OfferCreate</c> itself, against the node: the
/// offer's own checks - frozen, unfunded, expired, unauthorized, malformed - its
/// <c>OfferSequence</c>, the offers of owners the issuer no longer lets hold what they buy, and
/// the trust lines a crossing creates and deletes, each compared with what the node does.
/// </summary>
[TestClass]
public class TestIOfferCreateChecks
{
    private static IXrplClient client;
    private static BookCrossingHarness dex;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
        dex = new BookCrossingHarness(client, nodeType);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static OfferCreate Offer(XrplWallet taker, XrplAmount takerPays, XrplAmount takerGets, OfferCreateFlags flags = 0) => new OfferCreate
    {
        Account = taker.ClassicAddress,
        TakerPays = takerPays.ToCurrency(),
        TakerGets = takerGets.ToCurrency(),
        Flags = flags == 0 ? null : flags,
    };

    [TestMethod]
    public async Task MissingAuthRemovesTheOfferAndRefusesNewOnes()
    {
        // Offer_test's testMissingAuth: alice asks for USD before gw requires authorization.
        XrplWallet[] w = await dex.Wallets(3);
        XrplWallet gw = w[0], alice = w[1], bob = w[2];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Offer(alice, Drops(40_000_000), Amount(usd, "40"));
        await dex.SetFlag(gw, AccountSetAsfFlags.asfRequireAuth);
        await dex.TrustWith(gw, Iou("USD", bob), "100", TrustSetFlags.tfSetfAuth);
        await dex.Trust(bob, usd, "100");
        await dex.Pay(gw, bob.ClassicAddress, Amount(usd, "50"));

        // alice may not hold USD any more: her offer is deleted, not crossed, and bob's is placed.
        OfferCrossingResult crossed = await dex.CrossAndCompare(bob, Offer(bob, Drops(40_000_000), Amount(usd, "40")));
        Assert.AreEqual("tesSUCCESS", crossed.EngineResult);
        Assert.HasCount(1, crossed.Offers);
        Assert.IsTrue(crossed.Offers[0].Deleted);
        Assert.IsNotNull(crossed.PlacedTakerPays);

        // Without a line alice cannot ask for USD; with an unauthorized one neither.
        OfferCrossingResult noLine = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "40"), Drops(40_000_000)));
        Assert.AreEqual("tecNO_LINE", noLine.EngineResult);

        await dex.TrustWith(gw, Iou("USD", alice), "100", 0);
        OfferCrossingResult noAuth = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "40"), Drops(40_000_000)));
        Assert.AreEqual("tecNO_AUTH", noAuth.EngineResult);

        // Authorized, alice crosses bob's offer.
        await dex.TrustWith(gw, Iou("USD", alice), "100", TrustSetFlags.tfSetfAuth);
        OfferCrossingResult authorized = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "40"), Drops(40_000_000)));
        Assert.AreEqual("tesSUCCESS", authorized.EngineResult);
    }

    [TestMethod]
    public async Task FrozenUnfundedAndExpiredOffersAreRefused()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], gw2 = w[1], taker = w[2], maker = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        IssuedCurrency eur = Iou("EUR", gw2);
        await Task.WhenAll(dex.Issuer(gw), dex.Issuer(gw2));
        await dex.Trusts((taker, usd), (maker, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")));
        await dex.Offer(maker, Amount(usd, "10"), Drops(10_000_000));

        // The taker holds no EUR to give.
        OfferCrossingResult unfunded = await dex.CrossAndCompare(taker, Offer(taker, Amount(usd, "10"), Amount(eur, "10")));
        Assert.AreEqual("tecUNFUNDED_OFFER", unfunded.EngineResult);

        OfferCreate expired = Offer(taker, Amount(usd, "5"), Drops(5_000_000));
        expired.Expiration = RippleEpoch.AddSeconds(await dex.LastCloseTime() - 10);
        OfferCrossingResult late = await dex.CrossAndCompare(taker, expired);
        Assert.AreEqual("tecEXPIRED", late.EngineResult);

        await dex.SetFlag(gw, AccountSetAsfFlags.asfGlobalFreeze);
        OfferCrossingResult frozen = await dex.CrossAndCompare(taker, Offer(taker, Amount(usd, "5"), Drops(5_000_000)));
        Assert.AreEqual("tecFROZEN", frozen.EngineResult);
    }

    [TestMethod]
    public async Task MalformedOffersNeverReachALedger()
    {
        XrplWallet[] w = await dex.Wallets(2);
        IssuedCurrency usd = Iou("USD", w[0]);
        await dex.Issuer(w[0]);

        OfferCrossingResult both = await dex.CrossAndCompare(
            w[1], Offer(w[1], Amount(usd, "5"), Drops(5_000_000), OfferCreateFlags.tfImmediateOrCancel | OfferCreateFlags.tfFillOrKill));
        Assert.AreEqual("temINVALID_FLAG", both.EngineResult);

        OfferCrossingResult hybrid = await dex.CrossAndCompare(w[1], Offer(w[1], Amount(usd, "5"), Drops(5_000_000), OfferCreateFlags.tfHybrid));
        Assert.AreEqual("temINVALID_FLAG", hybrid.EngineResult);
    }

    [TestMethod]
    public async Task OfferSequenceCancelsTheOlderOfferFirst()
    {
        XrplWallet[] w = await dex.Wallets(3);
        XrplWallet gw = w[0], taker = w[1], maker = w[2];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((taker, usd), (maker, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, taker, Amount(usd, "50")));
        await dex.Offer(maker, Amount(usd, "10"), Drops(10_000_000));

        // The taker's resting offer to sell USD, replaced by one that buys USD from the maker.
        uint sequence = await dex.OfferWithSequence(taker, Amount(usd, "20"), Drops(40_000_000));
        OfferCreate replacing = Offer(taker, Amount(usd, "15"), Drops(15_000_000));
        replacing.OfferSequence = sequence;
        OfferCrossingResult result = await dex.CrossAndCompare(taker, replacing);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsTrue(result.Offers.Any(o => o.Account == taker.ClassicAddress && o.Deleted), "the older offer is cancelled");

        // An OfferSequence at or above the account's sequence is malformed.
        OfferCreate ahead = Offer(taker, Amount(usd, "1"), Drops(1_000_000));
        ahead.OfferSequence = sequence + 1_000;
        OfferCrossingResult refused = await dex.CrossAndCompare(taker, ahead);
        Assert.AreEqual("temBAD_SEQUENCE", refused.EngineResult);
    }

    [TestMethod]
    public async Task CrossingCreatesAndDeletesTrustLines()
    {
        // alice buys USD without a trust line, which the crossing creates at her expense; selling
        // it all back returns the line to its defaults, and it is deleted.
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], alice = w[1], maker = w[2], buyer = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((maker, usd), (buyer, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")));
        await dex.Offer(maker, Amount(usd, "10"), Drops(10_000_000));

        OfferCrossingResult bought = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "10"), Drops(10_000_000)));
        Assert.AreEqual("tesSUCCESS", bought.EngineResult);
        Assert.HasCount(1, bought.TrustLines);
        Assert.IsTrue(bought.TrustLines[0].Created);

        // Someone buys USD below alice's purchase price; she sells all of it to them.
        await dex.Offer(buyer, Drops(9_000_000), Amount(usd, "20"));
        OfferCrossingResult sold = await dex.CrossAndCompare(alice, Offer(alice, Drops(4_000_000), Amount(usd, "10"), OfferCreateFlags.tfSell));
        Assert.AreEqual("tesSUCCESS", sold.EngineResult);
        Assert.HasCount(1, sold.TrustLines);
        Assert.IsTrue(sold.TrustLines[0].Deleted);
    }
}
