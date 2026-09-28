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
/// <see cref="OfferCreateCrossing"/> on an IOU/IOU offer, where the node crosses the direct book
/// and, strand by strand, the two books of a bridge through XRP - compared with the node.
/// </summary>
[TestClass]
public class TestIBookCrossingBridge
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

    /// <summary>USD and EUR issuers, three makers holding both, and a taker holding USD.</summary>
    private static async Task<(XrplWallet[] Makers, XrplWallet Taker, IssuedCurrency Usd, IssuedCurrency Eur)> Market(uint? usdRate = null, uint? eurRate = null)
    {
        XrplWallet[] w = await dex.Wallets(6);
        IssuedCurrency usd = Iou("USD", w[0]);
        IssuedCurrency eur = Iou("EUR", w[1]);
        await Task.WhenAll(dex.Issuer(w[0], usdRate), dex.Issuer(w[1], eurRate));
        XrplWallet[] makers = { w[2], w[3], w[4] };
        await dex.Trusts((makers[0], eur), (makers[0], usd), (makers[1], eur), (makers[2], usd), (w[5], usd), (w[5], eur));
        await dex.Pays(
            (w[1], makers[0], Amount(eur, "1000")),
            (w[1], makers[1], Amount(eur, "1000")),
            (w[0], makers[2], Amount(usd, "1000")),
            (w[0], w[5], Amount(usd, "500")));
        return (makers, w[5], usd, eur);
    }

    [TestMethod]
    public async Task DirectBookAndBridgeInterleave()
    {
        (XrplWallet[] makers, XrplWallet taker, IssuedCurrency usd, IssuedCurrency eur) = await Market();

        // Direct: EUR for USD at 1.10 and 1.25. Bridge: USD -> XRP (maker 2 buys USD), XRP -> EUR
        // (maker 1 sells EUR), together about 1.17, so the engine alternates between the strands.
        await dex.Offer(makers[0], Amount(eur, "30"), Amount(usd, "33"));
        await dex.Offer(makers[0], Amount(eur, "40"), Amount(usd, "50"));
        await dex.Offer(makers[2], Drops(60_000_000), Amount(usd, "30"));
        await dex.Offer(makers[1], Amount(eur, "50"), Drops(117_000_000));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(eur, "90"), Amount(usd, "120"));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task BridgeWithTransferFeesOnBothSides()
    {
        (XrplWallet[] makers, XrplWallet taker, IssuedCurrency usd, IssuedCurrency eur) =
            await Market(usdRate: 1_003_000_000, eurRate: 1_002_000_000);

        await dex.Offer(makers[0], Amount(eur, "25.5"), Amount(usd, "30"));
        await dex.Offer(makers[2], Drops(40_000_000), Amount(usd, "19.9"));
        await dex.Offer(makers[1], Amount(eur, "22.2"), Drops(41_000_000));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(eur, "40"), Amount(usd, "50"), OfferCreateFlags.tfSell);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }
}
