using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="OfferCreateCrossing"/> with an AMM pool in the book: the pool alone, the pool
/// interleaved with order book offers, the auction slot's discounted fee, and the Fibonacci
/// offers a pool makes when the engine runs two strands - compared with the node.
/// </summary>
[TestClass]
public class TestIBookCrossingAmm
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

    /// <summary>A USD issuer, a pool creator holding USD, a maker holding USD and a taker.</summary>
    private static async Task<(XrplWallet Issuer, XrplWallet Creator, XrplWallet Maker, XrplWallet Taker, IssuedCurrency Usd)> Market(uint? transferRate = null)
    {
        XrplWallet[] w = await dex.Wallets(4);
        IssuedCurrency usd = Iou("USD", w[0]);
        await dex.Issuer(w[0], transferRate);
        await dex.Trusts((w[1], usd), (w[2], usd), (w[3], usd));
        await dex.Pays((w[0], w[1], Amount(usd, "20000")), (w[0], w[2], Amount(usd, "5000")));
        return (w[0], w[1], w[2], w[3], usd);
    }

    [TestMethod]
    public async Task PoolAlone()
    {
        (_, XrplWallet creator, _, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.CreateAmm(creator, Drops(200_000_000), Amount(usd, "202"), tradingFee: 250);

        // Buying USD with XRP from the pool only: one strand, the pool sized to the taker's price.
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "10"), Drops(11_000_000));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.HasCount(1, result.Pools);
    }

    [TestMethod]
    public async Task SinglePathLimitedToThePriceWithATransferFee()
    {
        // AMM_test's single-path case: the taker pays a 0.1% fee on the USD it gives, and the
        // pool's output is cut so the fee-inclusive quality meets the taker's price.
        (XrplWallet issuer, XrplWallet creator, _, XrplWallet taker, IssuedCurrency usd) = await Market(transferRate: 1_001_000_000);
        await dex.CreateAmm(creator, Drops(100_000_000), Amount(usd, "50"), tradingFee: 0);
        await dex.Pay(issuer, taker.ClassicAddress, Amount(usd, "300"));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Drops(10_000_000), Amount(usd, "5.5"));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsNotNull(result.PlacedTakerPays, "the rest of the offer is placed");
    }

    [TestMethod]
    public async Task PoolInterleavedWithTheBook()
    {
        (_, XrplWallet creator, XrplWallet maker, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.CreateAmm(creator, Drops(100_000_000), Amount(usd, "105"), tradingFee: 500);
        await dex.Offer(maker, Amount(usd, "2"), Drops(1_950_000));
        await dex.Offer(maker, Amount(usd, "3"), Drops(3_000_000));
        await dex.Offer(maker, Amount(usd, "2.5"), Drops(2_650_000));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "12"), Drops(13_000_000));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task SellIouIntoThePoolWithATransferFee()
    {
        (XrplWallet issuer, XrplWallet creator, XrplWallet maker, XrplWallet taker, IssuedCurrency usd) = await Market(transferRate: 1_004_000_000);
        await dex.Pay(issuer, taker.ClassicAddress, Amount(usd, "400"));
        await dex.CreateAmm(creator, Drops(200_000_000), Amount(usd, "200"), tradingFee: 1000);
        await dex.Offer(maker, Drops(5_000_000), Amount(usd, "5.3"));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Drops(15_000_000), Amount(usd, "17"), OfferCreateFlags.tfSell);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task AuctionSlotHolderPaysTheDiscountedFee()
    {
        (XrplWallet issuer, XrplWallet creator, _, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.CreateAmm(creator, Drops(300_000_000), Amount(usd, "300"), tradingFee: 1000);

        // The creator holds the pool's LP tokens; it gives the taker some to bid with.
        IssuedCurrency lpt = await dex.LpToken(Xrp, usd);
        await dex.Trust(taker, lpt);
        await dex.Pay(creator, taker.ClassicAddress, Amount(lpt, "1000"));
        await dex.Submit(new AMMBid { Account = taker.ClassicAddress, Asset = Xrp, Asset2 = usd }, taker);

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "10"), Drops(11_000_000), OfferCreateFlags.tfImmediateOrCancel);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task TwoStrandsMakeThePoolOfferFibonacciSlices()
    {
        // An EUR/USD pool with a bridge through XRP: two strands, so the pool offers slices.
        XrplWallet[] w = await dex.Wallets(6);
        IssuedCurrency usd = Iou("USD", w[0]);
        IssuedCurrency eur = Iou("EUR", w[1]);
        await Task.WhenAll(dex.Issuer(w[0]), dex.Issuer(w[1]));
        XrplWallet creator = w[2];
        XrplWallet makerUsd = w[3];
        XrplWallet makerEur = w[4];
        XrplWallet taker = w[5];
        await dex.Trusts((creator, usd), (creator, eur), (makerUsd, usd), (makerEur, eur), (taker, usd), (taker, eur));
        await dex.Pays(
            (w[0], creator, Amount(usd, "50000")),
            (w[1], creator, Amount(eur, "50000")),
            (w[0], makerUsd, Amount(usd, "100")),
            (w[1], makerEur, Amount(eur, "1000")),
            (w[0], taker, Amount(usd, "2000")));

        await dex.CreateAmm(creator, Amount(usd, "10000"), Amount(eur, "9000"), tradingFee: 300);
        await dex.Offer(makerUsd, Drops(100_000_000), Amount(usd, "50"));
        await dex.Offer(makerEur, Amount(eur, "44"), Drops(100_000_000));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(eur, "800"), Amount(usd, "1000"), OfferCreateFlags.tfImmediateOrCancel);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }
}
