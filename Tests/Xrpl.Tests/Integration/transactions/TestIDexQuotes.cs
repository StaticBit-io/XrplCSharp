using System.Collections.Generic;
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
/// Books deeper than one <c>book_offers</c> page, read by walking their directories, and the
/// quotes <see cref="DexQuoteSugar"/> computes - each checked against what the node then does.
/// </summary>
[TestClass]
public class TestIDexQuotes
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

    [TestMethod]
    public async Task ABookDeeperThanOnePage()
    {
        const int makers = 11;
        const int offersEach = 10;
        XrplWallet[] w = await dex.Wallets(makers + 3);
        XrplWallet gw = w[0], taker = w[1], dropped = w[2];
        XrplWallet[] makerWallets = w.Skip(3).ToArray();
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts(makerWallets.Append(dropped).Select(m => (m, usd)).ToArray());
        await dex.Pays(makerWallets.Append(dropped).Select(m => (gw, m, Amount(usd, "20"))).ToArray());

        // At the top of the book, an offer whose owner then gives its USD away: unfunded, which
        // book_offers leaves out and the node removes on the way.
        await dex.Offer(dropped, Amount(usd, "1"), Drops(900_000));
        await dex.Pay(dropped, gw.ClassicAddress, Amount(usd, "20"));

        // 110 funded offers, a price each.
        await Task.WhenAll(makerWallets.Select(async (maker, m) =>
        {
            for (int i = 0; i < offersEach; i++)
                await dex.Offer(maker, Amount(usd, "1"), Drops(1_000_000 + ((m * offersEach) + i) * 1_000));
        }));

        OfferCreate offer = new OfferCreate
        {
            Account = taker.ClassicAddress,
            TakerPays = Amount(usd, "115").ToCurrency(),
            TakerGets = Drops(230_000_000).ToCurrency(),
        };

        // One page, or fifty offers, does not cover a crossing that empties the book.
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        DexSnapshot onePage = await DexSnapshot.FromNodeAsync(client, offer);
        Assert.IsTrue(OfferCreateCrossing.Cross(onePage, offer, rules).NeedsDeeperBooks, "one page is not the whole book");
        DexSnapshot fifty = await DexSnapshot.FromNodeAsync(client, offer, new DexSnapshotOptions { BookDepth = 50 });
        Assert.HasCount(1, fifty.PartialBooks);
        Assert.IsTrue(OfferCreateCrossing.Cross(fifty, offer, rules).NeedsDeeperBooks, "fifty offers are not the whole book");

        // The quote reads deeper until the book is covered.
        OfferCrossingResult quote = await client.QuoteOfferCreateAsync(offer);
        Assert.IsFalse(quote.NeedsDeeperBooks, "the quote covers the book");

        // The whole book, the unfunded offer included, matches the node to the last drop.
        OfferCrossingResult result = await dex.CrossAndCompare(taker, offer, new DexSnapshotOptions { BookDepth = 500 });
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsFalse(result.NeedsDeeperBooks);
        Assert.AreEqual(makers * offersEach + 1, result.Offers.Count(o => o.Deleted), "every offer is taken or removed");
        Assert.AreEqual(quote.Paid, result.Paid, "the quote's cost");
        Assert.AreEqual(quote.Received, result.Received, "the quote's proceeds");
    }

    [TestMethod]
    public async Task QuotesToDeliverAndToSpend()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet gw = w[0], maker = w[1], maker2 = w[2], sender = w[3], receiver = w[4];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw, transferRate: 1_002_000_000);
        await dex.Trusts((maker, usd), (maker2, usd), (receiver, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, maker2, Amount(usd, "100")));
        await dex.Offer(maker, Amount(usd, "15"), Drops(7_000_000));
        await dex.Offer(maker2, Amount(usd, "15"), Drops(8_000_000));

        // Exactly 20 USD delivered: the quoted cost is what the node takes.
        PaymentQuote deliver = await client.QuoteDeliverAsync(sender.ClassicAddress, receiver.ClassicAddress, Amount(usd, "20"), Xrp);
        Assert.AreEqual("tesSUCCESS", deliver.Result.EngineResult);
        Assert.AreEqual(Amount(usd, "20"), deliver.Delivered);

        // The local path finder, over the ledger's books, quotes the same.
        BookIndex books = await BookIndex.FromLedgerAsync(client);
        PaymentQuote local = await client.QuoteDeliverAsync(sender.ClassicAddress, receiver.ClassicAddress, Amount(usd, "20"), Xrp, books: books);
        Assert.AreEqual(deliver.Cost, local.Cost, "the local search's cost");
        Assert.HasCount(deliver.Payment.Paths?.Count ?? 0, local.Payment.Paths ?? new List<List<PathStep>>());
        PaymentFlowResult paid = await dex.PayAndCompare(sender, deliver.Payment);
        Assert.AreEqual("tesSUCCESS", paid.EngineResult);
        Assert.AreEqual(deliver.Delivered, paid.DeliveredAmount);
        Assert.AreEqual(deliver.Cost, paid.Paid);

        // Exactly 3 XRP spent: the quoted delivery is what the node delivers.
        PaymentQuote spend = await client.QuoteSpendAsync(sender.ClassicAddress, receiver.ClassicAddress, Drops(3_000_000), usd);
        Assert.AreEqual("tesSUCCESS", spend.Result.EngineResult);
        PaymentFlowResult spent = await dex.PayAndCompare(sender, spend.Payment);
        Assert.AreEqual("tesSUCCESS", spent.EngineResult);
        Assert.AreEqual(spend.Delivered, spent.DeliveredAmount);
    }
}
