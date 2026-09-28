using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="PaymentFlow"/> against the node: payments along trust lines, through intermediaries
/// with qualities and transfer fees, through order books and pools, with SendMax, DeliverMin,
/// partial payment and the limit quality - and the ones the node refuses - each compared with what
/// the node does.
/// </summary>
[TestClass]
public class TestIPaymentFlow
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

    private static Payment Pay(XrplWallet from, XrplWallet to, XrplAmount amount) => new Payment
    {
        Account = from.ClassicAddress,
        Destination = to.ClassicAddress,
        Amount = amount.ToCurrency(),
    };

    private static List<List<PathStep>> Paths(params PathStep[][] paths)
    {
        List<List<PathStep>> result = new List<List<PathStep>>();
        foreach (PathStep[] path in paths)
            result.Add(new List<PathStep>(path));
        return result;
    }

    [TestMethod]
    public async Task HolderToHolderThroughTheIssuerWithATransferFee()
    {
        XrplWallet[] w = await dex.Wallets(3);
        IssuedCurrency usd = Iou("USD", w[0]);
        await dex.Issuer(w[0], transferRate: 1_002_000_000);
        await dex.Trusts((w[1], usd), (w[2], usd));
        await dex.Pays((w[0], w[1], Amount(usd, "100")));

        // The sender pays 0.2% on top; without SendMax the amount is its own limit, so it is short.
        PaymentFlowResult failed = await dex.PayAndCompare(w[1], Pay(w[1], w[2], Amount(usd, "10")));
        Assert.AreEqual("tecPATH_PARTIAL", failed.EngineResult);

        Payment withSendMax = Pay(w[1], w[2], Amount(usd, "10"));
        withSendMax.SendMax = Amount(usd, "10.1").ToCurrency();
        PaymentFlowResult result = await dex.PayAndCompare(w[1], withSendMax);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task RipplingThroughIntermediariesWithQualities()
    {
        // a holds m1's IOU, m1 holds m2's IOU, b trusts m2: a pays b along a -> m1 -> m2 -> b.
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet a = w[0], m1 = w[1], m2 = w[2], b = w[3];
        IssuedCurrency ofM1 = Iou("IOU", m1);
        IssuedCurrency ofM2 = Iou("IOU", m2);
        await Task.WhenAll(dex.Issuer(m1), dex.Issuer(m2, transferRate: 1_003_000_000));
        await Task.WhenAll(
            dex.TrustWith(a, ofM1, "1000", TrustSetFlags.tfClearNoRipple),
            dex.TrustWith(m1, ofM2, "1000", TrustSetFlags.tfClearNoRipple),
            dex.TrustWith(b, ofM2, "1000", TrustSetFlags.tfClearNoRipple));
        await dex.Pays((m1, a, Amount(ofM1, "200")), (m2, m1, Amount(ofM2, "300")));

        // The qualities once the lines are funded, so funding them is not discounted.
        await Task.WhenAll(
            dex.TrustWith(a, ofM1, "1000", TrustSetFlags.tfClearNoRipple, qualityOut: 1_010_000_000),
            dex.TrustWith(m1, ofM2, "1000", TrustSetFlags.tfClearNoRipple, qualityIn: 995_000_000, qualityOut: 1_004_000_000),
            dex.TrustWith(b, ofM2, "1000", TrustSetFlags.tfClearNoRipple, qualityIn: 990_000_000));

        Payment payment = Pay(a, b, Amount(ofM2, "25"));
        payment.SendMax = Amount(new IssuedCurrency { Currency = "IOU", Issuer = a.ClassicAddress }, "40").ToCurrency();
        payment.Paths = Paths(new[] { new PathStep { Account = m1.ClassicAddress } });
        PaymentFlowResult result = await dex.PayAndCompare(a, payment);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task NoRippleOnBothLinesStopsThePayment()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet a = w[0], m1 = w[1], m2 = w[2], b = w[3];
        IssuedCurrency ofM1 = Iou("IOU", m1);
        IssuedCurrency ofM2 = Iou("IOU", m2);
        await Task.WhenAll(dex.Issuer(m1), dex.Issuer(m2));
        await Task.WhenAll(
            dex.TrustWith(a, ofM1, "1000", TrustSetFlags.tfClearNoRipple),
            dex.TrustWith(m1, ofM2, "1000", TrustSetFlags.tfSetNoRipple),
            dex.TrustWith(b, ofM2, "1000", TrustSetFlags.tfClearNoRipple));

        // m1 sets NoRipple on its line to a as well, before it owes a anything: nothing may ripple through it.
        await dex.TrustWith(m1, Iou("IOU", a), "0", TrustSetFlags.tfSetNoRipple);
        await dex.Pays((m1, a, Amount(ofM1, "200")), (m2, m1, Amount(ofM2, "300")));

        Payment payment = Pay(a, b, Amount(ofM2, "25"));
        payment.SendMax = Amount(new IssuedCurrency { Currency = "IOU", Issuer = a.ClassicAddress }, "40").ToCurrency();
        payment.Paths = Paths(new[] { new PathStep { Account = m1.ClassicAddress } });
        payment.Flags = PaymentFlags.tfNoDirectRipple;
        PaymentFlowResult result = await dex.PayAndCompare(a, payment);
        Assert.AreEqual("tecPATH_DRY", result.EngineResult);
    }

    [TestMethod]
    public async Task CrossCurrencyThroughABookWithDeliverMin()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], maker = w[1], sender = w[2], receiver = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw, transferRate: 1_001_000_000);
        await dex.Trusts((maker, usd), (receiver, usd));
        await dex.Pays((gw, maker, Amount(usd, "500")));
        await dex.Offer(maker, Amount(usd, "20"), Drops(10_000_000));
        await dex.Offer(maker, Amount(usd, "30"), Drops(18_000_000));

        // XRP in, USD out: partial, at most 25 XRP, at least 40 USD delivered.
        Payment payment = Pay(sender, receiver, Amount(usd, "60"));
        payment.SendMax = Drops(25_000_000).ToCurrency();
        payment.DeliverMin = Amount(usd, "40").ToCurrency();
        payment.Flags = PaymentFlags.tfPartialPayment;
        PaymentFlowResult result = await dex.PayAndCompare(sender, payment);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);

        payment.DeliverMin = Amount(usd, "59").ToCurrency();
        PaymentFlowResult short_ = await dex.PayAndCompare(sender, payment);
        Assert.AreEqual("tecPATH_PARTIAL", short_.EngineResult);
    }

    [TestMethod]
    public async Task IouToIouThroughTheXrpBridgeAndAPool()
    {
        XrplWallet[] w = await dex.Wallets(6);
        XrplWallet gUsd = w[0], gEur = w[1], makerUsd = w[2], creator = w[3], sender = w[4], receiver = w[5];
        IssuedCurrency usd = Iou("USD", gUsd);
        IssuedCurrency eur = Iou("EUR", gEur);
        await Task.WhenAll(dex.Issuer(gUsd, transferRate: 1_002_000_000), dex.Issuer(gEur));
        await dex.Trusts((makerUsd, usd), (creator, eur), (sender, usd), (receiver, eur));
        await dex.Pays((gUsd, sender, Amount(usd, "300")), (gEur, creator, Amount(eur, "1000")));

        // USD -> XRP from an order book, XRP -> EUR from a pool.
        await dex.Offer(makerUsd, Drops(100_000_000), Amount(usd, "50"));
        await dex.CreateAmm(creator, Drops(150_000_000), Amount(eur, "70"), tradingFee: 300);

        Payment payment = Pay(sender, receiver, Amount(eur, "20"));
        payment.SendMax = Amount(usd, "60").ToCurrency();
        payment.Paths = Paths(new[] { new PathStep { CurrencyCode = "XRP" } });
        PaymentFlowResult result = await dex.PayAndCompare(sender, payment);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task LimitQualityAgainstAPool()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], creator = w[1], sender = w[2], receiver = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((creator, usd), (receiver, usd));
        await dex.Pays((gw, creator, Amount(usd, "1000")));
        await dex.CreateAmm(creator, Drops(100_000_000), Amount(usd, "101"), tradingFee: 0);

        // At most 10 XRP for 10 USD, taken only at that price or better: the pool is cut to it.
        Payment payment = Pay(sender, receiver, Amount(usd, "10"));
        payment.SendMax = Drops(10_000_000).ToCurrency();
        payment.Flags = PaymentFlags.tfPartialPayment | PaymentFlags.tfLimitQuality | PaymentFlags.tfNoDirectRipple;
        payment.Paths = Paths(new[] { new PathStep { CurrencyCode = "USD", Issuer = gw.ClassicAddress } });
        PaymentFlowResult result = await dex.PayAndCompare(sender, payment);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task PathsFromRipplePathFind()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet gw = w[0], maker = w[1], sender = w[2], receiver = w[3], maker2 = w[4];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((maker, usd), (maker2, usd), (receiver, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, maker2, Amount(usd, "100")));
        await dex.Offer(maker, Amount(usd, "15"), Drops(7_000_000));
        await dex.Offer(maker2, Amount(usd, "15"), Drops(8_000_000));

        RipplePathFindRequest find = new RipplePathFindRequest(sender.ClassicAddress, receiver.ClassicAddress, Amount(usd, "20").ToCurrency())
        {
            SourceCurrencies = new List<SourceCurrency> { new SourceCurrency { Currency = "XRP" } },
        };
        RipplePathFindResponse found = await client.RipplePathFind(find).Typed();
        Assert.IsNotEmpty(found.Alternatives, "a path is found");

        Payment payment = Pay(sender, receiver, Amount(usd, "20"));
        payment.SendMax = found.Alternatives[0].SourceAmount;
        payment.Paths = found.Alternatives[0].PathsComputed;
        PaymentFlowResult result = await dex.PayAndCompare(sender, payment);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
    }

    [TestMethod]
    public async Task RefusedPayments()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], sender = w[1], tagged = w[2], guarded = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await Task.WhenAll(dex.SetFlag(tagged, AccountSetAsfFlags.asfRequireDest), dex.SetFlag(guarded, AccountSetAsfFlags.asfDepositAuth));

        Assert.AreEqual("tecDST_TAG_NEEDED", (await dex.PayAndCompare(sender, Pay(sender, tagged, Drops(1_000_000)))).EngineResult);
        Assert.AreEqual("tecNO_PERMISSION", (await dex.PayAndCompare(sender, Pay(sender, guarded, Drops(20_000_000)))).EngineResult);
        Assert.AreEqual("tecUNFUNDED_PAYMENT", (await dex.PayAndCompare(sender, Pay(sender, guarded, Drops(399_000_000)))).EngineResult);

        // No trust line from the sender to the issuer: nothing can flow.
        Assert.AreEqual("tecPATH_DRY", (await dex.PayAndCompare(sender, Pay(sender, gw, Amount(usd, "5")))).EngineResult);

        XrplWallet fresh = XrplWallet.Generate();
        Payment create = new Payment { Account = sender.ClassicAddress, Destination = fresh.ClassicAddress, Amount = Drops(1_000).ToCurrency() };
        Assert.AreEqual("tecNO_DST_INSUF_XRP", (await dex.PayAndCompare(sender, create)).EngineResult);

        create.Amount = Drops(20_000_000).ToCurrency();
        Assert.AreEqual("tesSUCCESS", (await dex.PayAndCompare(sender, create)).EngineResult);
    }
}
