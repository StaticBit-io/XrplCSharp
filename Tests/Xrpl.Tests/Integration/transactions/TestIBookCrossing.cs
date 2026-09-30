using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="OfferCreateCrossing"/> over order books without a pool: several offers and quality
/// levels, transfer fees, owners who cannot pay, and each <c>OfferCreate</c> flag - every case
/// compared with what the node does.
/// </summary>
[TestClass]
public class TestIBookCrossing
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

    /// <summary>An issuer, two funded makers and a taker with a trust line.</summary>
    private static async Task<(XrplWallet Issuer, XrplWallet Maker1, XrplWallet Maker2, XrplWallet Taker, IssuedCurrency Usd)> Market(
        uint? transferRate = null,
        string maker1Funds = "1000",
        string maker2Funds = "1000")
    {
        XrplWallet[] w = await dex.Wallets(4);
        IssuedCurrency usd = Iou("USD", w[0]);
        await dex.Issuer(w[0], transferRate);
        await dex.Trusts((w[1], usd), (w[2], usd), (w[3], usd));
        await dex.Pays((w[0], w[1], Amount(usd, maker1Funds)), (w[0], w[2], Amount(usd, maker2Funds)));
        return (w[0], w[1], w[2], w[3], usd);
    }

    [TestMethod]
    public async Task BuyAcrossSeveralQualityLevels()
    {
        (_, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.Offer(m1, Amount(usd, "100"), Drops(30_000_000));
        await dex.Offer(m2, Amount(usd, "70"), Drops(21_000_000));
        await dex.Offer(m1, Amount(usd, "50"), Drops(16_000_000));
        await dex.Offer(m2, Amount(usd, "200"), Drops(70_000_000));

        // 0.34 XRP per USD: the 0.30 and 0.32 levels cross, the 0.35 one does not; the rest is placed.
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "250"), Drops(85_000_000));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsNotNull(result.PlacedTakerPays, "the remainder is placed");
    }

    [TestMethod]
    public async Task TransferFeeAndOwnersWhoCannotPay()
    {
        (XrplWallet issuer, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) =
            await Market(transferRate: 1_002_500_000, maker1Funds: "40", maker2Funds: "500");
        XrplWallet[] extra = await dex.Wallets(1);
        await dex.Fund(issuer, extra[0], usd, "300");

        // m1 offers more than it holds; extra's offer loses its funding before the crossing.
        await dex.Offer(m1, Amount(usd, "100"), Drops(30_000_000));
        await dex.Offer(extra[0], Amount(usd, "100"), Drops(30_500_000));
        await dex.Offer(m2, Amount(usd, "123.456789"), Drops(40_000_000));
        await dex.Pay(extra[0], issuer.ClassicAddress, Amount(usd, "300"));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "150"), Drops(60_000_000), OfferCreateFlags.tfImmediateOrCancel);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task SellTakesMoreThanAskedFor()
    {
        (_, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.Offer(m1, Amount(usd, "10"), Drops(2_000_000));
        await dex.Offer(m2, Amount(usd, "30"), Drops(7_000_000));

        // Selling 5 XRP for at least 15 USD: the book pays more than 15.
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "15"), Drops(5_000_000), OfferCreateFlags.tfSell);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task SellIouPayingTheTransferFee()
    {
        // The taker sells USD it holds, so it pays the issuer's fee on top of what reaches the offers.
        (XrplWallet issuer, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) = await Market(transferRate: 1_005_000_000);
        await dex.Pay(issuer, taker.ClassicAddress, Amount(usd, "100"));
        await dex.Offer(m1, Drops(10_000_000), Amount(usd, "31"));
        await dex.Offer(m2, Drops(20_000_000), Amount(usd, "66.6"));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Drops(25_000_000), Amount(usd, "80"));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task PassiveDoesNotCrossAnEqualOffer()
    {
        (_, XrplWallet m1, _, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.Offer(m1, Amount(usd, "10"), Drops(3_000_000));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "10"), Drops(3_000_000), OfferCreateFlags.tfPassive);
        Assert.IsEmpty(result.Offers, "an offer of the same quality is left alone");
        Assert.IsNotNull(result.PlacedTakerPays);
    }

    [TestMethod]
    public async Task FillOrKillIsKilledAndImmediateOrCancelWithNothingToCross()
    {
        (_, XrplWallet m1, _, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.Offer(m1, Amount(usd, "10"), Drops(3_000_000));

        OfferCrossingResult killed = await dex.CrossAndCompare(taker, Amount(usd, "20"), Drops(6_000_000), OfferCreateFlags.tfFillOrKill);
        Assert.AreEqual("tecKILLED", killed.EngineResult);

        OfferCrossingResult nothing = await dex.CrossAndCompare(taker, Amount(usd, "20"), Drops(1_000_000), OfferCreateFlags.tfImmediateOrCancel);
        Assert.AreEqual("tecKILLED", nothing.EngineResult);
    }

    [TestMethod]
    public async Task OwnOfferAtTheTakersPriceIsRemoved()
    {
        (XrplWallet issuer, XrplWallet m1, _, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.Pay(issuer, taker.ClassicAddress, Amount(usd, "50"));

        // The taker's own offer sells USD at 0.25; its new offer buys USD at 0.30 and deletes it.
        await dex.Offer(taker, Amount(usd, "20"), Drops(5_000_000));
        await dex.Offer(m1, Amount(usd, "20"), Drops(5_500_000));

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "30"), Drops(9_000_000));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task ExpiredOfferIsRemoved()
    {
        (_, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) = await Market();
        // Ledger time, not the machine's clock: a standalone node's close times run their own course.
        uint expiration = await dex.LastCloseTime() + 90;
        await dex.Offer(m1, Amount(usd, "10"), Drops(2_000_000), expiration: expiration);
        await dex.Offer(m2, Amount(usd, "10"), Drops(2_500_000));

        // Wait until a validated ledger closed after the expiration, so both sides see it expired.
        await dex.WaitForCloseAfter(expiration);

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "10"), Drops(3_000_000));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task OwnerFundsFromTheBookCrossAsTheNodeDoes()
    {
        // As TransferFeeAndOwnersWhoCannotPay, with the makers built from owner_funds.
        (XrplWallet issuer, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) =
            await Market(transferRate: 1_002_500_000, maker1Funds: "40", maker2Funds: "500");
        await dex.Offer(m1, Amount(usd, "100"), Drops(30_000_000));
        await dex.Offer(m2, Amount(usd, "123.456789"), Drops(40_000_000));
        DexSnapshotOptions fromBook = new DexSnapshotOptions { OwnerFundsFromBook = true };

        DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(dex.Client, taker.ClassicAddress, usd, Xrp, fromBook);
        CollectionAssert.AreEquivalent(new[] { m1.ClassicAddress, m2.ClassicAddress }, snapshot.ApproximatedOwners.ToArray());
        Assert.IsFalse(snapshot.ApproximatedOwners.Contains(issuer.ClassicAddress), "the issuer is read");

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Amount(usd, "150"), Drops(60_000_000), OfferCreateFlags.tfImmediateOrCancel, fromBook);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task UnlimitedTakerQuotesAsAFundedOne()
    {
        (_, XrplWallet m1, XrplWallet m2, XrplWallet taker, IssuedCurrency usd) = await Market();
        await dex.Offer(m1, Amount(usd, "100"), Drops(30_000_000));
        await dex.Offer(m2, Amount(usd, "70"), Drops(21_000_000));

        OfferCreate Order(string account) => new OfferCreate
        {
            Account = account,
            TakerPays = Amount(usd, "150").ToCurrency(),
            TakerGets = Drops(50_000_000).ToCurrency(),
            Flags = OfferCreateFlags.tfImmediateOrCancel,
        };

        OfferCrossingResult funded = await dex.Client.QuoteOfferCreateAsync(Order(taker.ClassicAddress));
        OfferCrossingResult anyone = await dex.Client.QuoteOfferCreateAsync(Order(DexSnapshot.UnlimitedTaker));
        Assert.AreEqual("tesSUCCESS", anyone.EngineResult);
        Assert.AreEqual(funded.Paid, anyone.Paid);
        Assert.AreEqual(funded.Received, anyone.Received);
        CollectionAssert.AreEqual(funded.Fills.Select(f => (f.OfferIndex, f.In, f.Out)).ToList(), anyone.Fills.Select(f => (f.OfferIndex, f.In, f.Out)).ToList());

        // A payment quoted from the unlimited taker delivers as one from a funded sender.
        XrplWallet sender = (await dex.Wallets(1))[0];
        Payment Pay(string from) => new Payment
        {
            Account = from,
            Destination = taker.ClassicAddress,
            Amount = Amount(usd, "50").ToCurrency(),
            SendMax = Drops(20_000_000).ToCurrency(),
        };

        PaymentFlowResult paid = await dex.Client.QuotePaymentAsync(Pay(sender.ClassicAddress));
        PaymentFlowResult quoted = await dex.Client.QuotePaymentAsync(Pay(DexSnapshot.UnlimitedTaker));
        Assert.AreEqual("tesSUCCESS", quoted.EngineResult);
        Assert.AreEqual(paid.DeliveredAmount, quoted.DeliveredAmount);
        Assert.AreEqual(paid.Paid, quoted.Paid);
    }

    [TestMethod]
    public async Task SnapshotReadsTheLedgerAsked()
    {
        (_, XrplWallet m1, _, XrplWallet taker, IssuedCurrency usd) = await Market();
        DexSnapshot before = await DexSnapshot.FromNodeAsync(dex.Client, taker.ClassicAddress, usd, Xrp);
        await dex.Offer(m1, Amount(usd, "10"), Drops(3_000_000));

        DexSnapshot after = await DexSnapshot.FromNodeAsync(dex.Client, taker.ClassicAddress, usd, Xrp);
        DexSnapshot replay = await DexSnapshot.FromNodeAsync(
            dex.Client,
            taker.ClassicAddress,
            usd,
            Xrp,
            new DexSnapshotOptions { Ledger = new LedgerIndex(before.LedgerSequence) });

        Assert.IsGreaterThan(before.LedgerSequence, after.LedgerSequence);
        Assert.IsTrue(after.Offers.Any(o => o.Account == m1.ClassicAddress), "the offer is in the book now");
        Assert.AreEqual(before.LedgerSequence, replay.LedgerSequence);
        Assert.IsFalse(replay.Offers.Any(o => o.Account == m1.ClassicAddress), "the ledger asked for is read, before the offer");
    }

    [TestMethod]
    public async Task TickSizeRoundsTheOffer()
    {
        XrplWallet[] w = await dex.Wallets(3);
        IssuedCurrency usd = Iou("USD", w[0]);
        await dex.Issuer(w[0], tickSize: 5);
        await Task.WhenAll(dex.Fund(w[0], w[1], usd, "1000"), dex.Trust(w[2], usd));
        await dex.Offer(w[1], Amount(usd, "3.33333"), Drops(1_000_000));

        OfferCrossingResult result = await dex.CrossAndCompare(w[2], Amount(usd, "7.777777"), Drops(2_345_678));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }
}
