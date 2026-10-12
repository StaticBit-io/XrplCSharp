using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

using BookOffer = Xrpl.Models.Transactions.Offer;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// MPTs on the order book (MPTokensV2, XLS-82): offers pairing an MPT with XRP and with an issued
/// currency that cross, <c>book_offers</c> reading an MPT book, and a cross-currency payment
/// delivering an MPT through it.
/// </summary>
/// <remarks>
/// The outcomes are checked against amounts worked out by hand rather than against
/// <see cref="OfferCreateCrossing"/> and <see cref="PaymentFlow"/>, which do not model MPT books
/// and refuse them with <see cref="NotSupportedException"/>. Every crossing here is at the resting
/// offer's own quality, so the amounts moved are exact.
/// </remarks>
[TestClass]
[TestCategory("DEX")]
public class TestIMptDex
{
    public TestContext TestContext { get; set; }

    private static IXrplClient client;
    private static BookCrossingHarness dex;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;
    private static bool mptokensV2Usable;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
        dex = new BookCrossingHarness(client, nodeType);
        // As TestIAMMMpt: on a standalone stand MPTokensV2 is a [features] Rules preset the
        // on-ledger guard cannot see, so the tests run there and a missing preset fails loudly.
        mptokensV2Usable = IntegrationTestConfig.IsStandalone(nodeType)
            || await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.MPTokensV2);
    }

    [TestInitialize]
    public void CheckMPTokensV2Amendment()
    {
        if (!mptokensV2Usable)
            Assert.Inconclusive("MPTokensV2 (XLS-82, MPTs on the DEX) is not enabled on the test node.");
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static IssuedCurrency MptAsset(string issuanceId) => new IssuedCurrency { MptIssuanceId = issuanceId };

    private static Currency Mpt(string issuanceId, string value) => new Currency { Value = value, MPTokenIssuanceID = issuanceId };

    private static Currency Drops(long drops) => new Currency { Value = drops.ToString(CultureInfo.InvariantCulture) };

    private static Currency Iou(IssuedCurrency asset, string value) => new Currency { CurrencyCode = asset.Currency, Issuer = asset.Issuer, Value = value };

    /// <summary>An MPT that can be traded and transferred; each holder is authorized and given <paramref name="units"/>.</summary>
    private static async Task<string> IssueMpt(XrplWallet issuer, IEnumerable<XrplWallet> holders, string units)
    {
        TransactionSummary created = await dex.SubmitWithMeta(
            new MPTokenIssuanceCreate
            {
                Account = issuer.ClassicAddress,
                Flags = MPTokenIssuanceCreateFlags.tfMPTCanTrade | MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
            },
            issuer);
        string issuanceId = created.Meta.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID in the metadata");

        foreach (XrplWallet holder in holders)
        {
            await dex.Submit(new MPTokenAuthorize { Account = holder.ClassicAddress, MPTokenIssuanceID = issuanceId }, holder);
            if (units != "0")
                await dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = holder.ClassicAddress, Amount = Mpt(issuanceId, units) }, issuer);
        }

        return issuanceId;
    }

    /// <summary>The account's MPT balance; zero when it holds none.</summary>
    private static async Task<ulong> MptBalance(string account, string issuanceId)
    {
        AccountObjects objects = await client.AccountObjects(new AccountObjectsRequest(account) { Type = LedgerEntryType.MPToken }).Typed();
        LOMPToken token = objects.AccountObjectList?.OfType<LOMPToken>()
            .FirstOrDefault(t => string.Equals(t.MPTokenIssuanceID, issuanceId, StringComparison.OrdinalIgnoreCase));
        return token?.MPTAmount ?? 0;
    }

    private static async Task<long> XrpDrops(string account) =>
        (long)(decimal.Parse(await client.GetXrpBalance(account), NumberStyles.Float, CultureInfo.InvariantCulture) * 1_000_000m);

    private static async Task<decimal> IouBalance(string account, IssuedCurrency asset)
    {
        AccountLines lines = await client.AccountLines(new AccountLinesRequest(account) { Peer = asset.Issuer }).Typed();
        TrustLine line = lines.TrustLines.Single(l => l.Currency == asset.Currency);
        return decimal.Parse(line.Balance, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public async Task TestIOfferCreate_MptForXrp_CrossesTheRestingOffer()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { maker, taker }, "0");
        await dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = maker.ClassicAddress, Amount = Mpt(mpt, "1000") }, issuer);

        // The maker sells 100 units at 10 000 drops each.
        await dex.Submit(new OfferCreate { Account = maker.ClassicAddress, TakerGets = Mpt(mpt, "100"), TakerPays = Drops(1_000_000) }, maker);
        long makerXrpBefore = await XrpDrops(maker.ClassicAddress);

        // The taker buys 40 of them at the same price.
        TransactionSummary crossed = await dex.SubmitWithMeta(
            new OfferCreate { Account = taker.ClassicAddress, TakerPays = Mpt(mpt, "40"), TakerGets = Drops(400_000) },
            taker);

        Assert.AreEqual(40ul, await MptBalance(taker.ClassicAddress, mpt), "the taker received 40 units");
        Assert.AreEqual(960ul, await MptBalance(maker.ClassicAddress, mpt), "the maker gave 40 of its 1000 units");
        Assert.AreEqual(makerXrpBefore + 400_000, await XrpDrops(maker.ClassicAddress), "the maker received 400 000 drops");

        AffectedNode makerOffer = crossed.Meta.AffectedNodes.Single(n => n.ModifiedNode?.LedgerEntryType == LedgerEntryType.Offer);
        Assert.IsTrue(makerOffer.ModifiedNode.TryGetFinal(out LOOffer remaining), "the maker's offer remains");
        Assert.AreEqual(XrplAmount.Parse(MptAsset(mpt), "60"), remaining.TakerGets.ToXrplAmount(), "the maker's offer still gives 60 units");
        Assert.AreEqual(XrplAmount.Parse(BookCrossingHarness.Xrp, "600000"), remaining.TakerPays.ToXrplAmount(), "the maker's offer still asks 600 000 drops");
        Assert.IsFalse(crossed.Meta.AffectedNodes.Any(n => n.CreatedNode?.LedgerEntryType == LedgerEntryType.Offer), "the taker's offer is filled, nothing is placed");
    }

    [TestMethod]
    public async Task TestIOfferCreate_MptForIou_ConsumesTheRestingOffer()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        IssuedCurrency usd = BookCrossingHarness.Iou("USD", issuer);
        await dex.Issuer(issuer);
        await dex.Fund(issuer, taker, usd, "100");
        await dex.Trust(maker, usd);
        string mpt = await IssueMpt(issuer, new[] { maker, taker }, "0");
        await dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = maker.ClassicAddress, Amount = Mpt(mpt, "50") }, issuer);

        // The maker sells all 50 units for 5 USD; the taker buys them, offering more than is on the book.
        await dex.Submit(new OfferCreate { Account = maker.ClassicAddress, TakerGets = Mpt(mpt, "50"), TakerPays = Iou(usd, "5") }, maker);
        TransactionSummary crossed = await dex.SubmitWithMeta(
            new OfferCreate { Account = taker.ClassicAddress, TakerPays = Mpt(mpt, "80"), TakerGets = Iou(usd, "8") },
            taker);

        Assert.AreEqual(50ul, await MptBalance(taker.ClassicAddress, mpt), "the taker received all 50 units");
        Assert.AreEqual(0ul, await MptBalance(maker.ClassicAddress, mpt), "the maker gave all its units");
        Assert.AreEqual(95m, await IouBalance(taker.ClassicAddress, usd), "the taker paid 5 USD");
        Assert.AreEqual(5m, await IouBalance(maker.ClassicAddress, usd), "the maker received 5 USD");

        Assert.IsTrue(
            crossed.Meta.AffectedNodes.Any(n => n.DeletedNode?.LedgerEntryType == LedgerEntryType.Offer),
            "the maker's offer is consumed");
        AffectedNode placed = crossed.Meta.AffectedNodes.Single(n => n.CreatedNode?.LedgerEntryType == LedgerEntryType.Offer);
        Assert.IsTrue(placed.CreatedNode.TryGetNew(out LOOffer rest), "the taker's remainder is placed");
        Assert.AreEqual(XrplAmount.Parse(MptAsset(mpt), "30"), rest.TakerPays.ToXrplAmount(), "the remainder asks the 30 units left");
        Assert.AreEqual(XrplAmount.Parse(usd, "3"), rest.TakerGets.ToXrplAmount(), "the remainder gives the 3 USD left");
    }

    [TestMethod]
    public async Task TestIBookOffers_ReadsAnMptBook()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet other) = (wallets[0], wallets[1], wallets[2]);
        IssuedCurrency usd = BookCrossingHarness.Iou("USD", issuer);
        await dex.Issuer(issuer);
        await dex.Fund(issuer, maker, usd, "100");
        string mpt = await IssueMpt(issuer, new[] { maker, other }, "500");

        // Two offers selling the MPT for XRP, the cheaper one placed second, and one buying it for USD.
        await dex.Submit(new OfferCreate { Account = maker.ClassicAddress, TakerGets = Mpt(mpt, "100"), TakerPays = Drops(2_000_000) }, maker);
        await dex.Submit(new OfferCreate { Account = other.ClassicAddress, TakerGets = Mpt(mpt, "100"), TakerPays = Drops(1_000_000) }, other);
        await dex.Submit(new OfferCreate { Account = maker.ClassicAddress, TakerGets = Iou(usd, "10"), TakerPays = Mpt(mpt, "200") }, maker);

        BookOffers sell = await client.BookOffers(new BookOffersRequest
        {
            TakerGets = new TakerAmount { MptIssuanceId = mpt },
            TakerPays = new TakerAmount { Currency = "XRP" },
            LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
        }).Typed();
        Assert.HasCount(2, sell.Offers, "both MPT/XRP offers are on the book");
        Assert.AreEqual(other.ClassicAddress, sell.Offers[0].Account, "the cheaper offer comes first");
        Assert.AreEqual(maker.ClassicAddress, sell.Offers[1].Account);
        foreach (BookOffer offer in sell.Offers)
        {
            Assert.AreEqual(AmountKind.Mpt, offer.TakerGets.ToXrplAmount().Kind);
            Assert.AreEqual(mpt, offer.TakerGets.MPTokenIssuanceID, ignoreCase: true);
            Assert.AreEqual("100", offer.TakerGets.Value);
            Assert.AreEqual(AmountKind.Xrp, offer.TakerPays.ToXrplAmount().Kind);
        }

        Assert.AreEqual("1000000", sell.Offers[0].TakerPays.Value);
        Assert.AreEqual(10000m, sell.Offers[0].Quality, "10 000 drops a unit");
        Assert.AreEqual(XrplQuality.FromAmounts(sell.Offers[0].TakerPays.ToXrplAmount(), sell.Offers[0].TakerGets.ToXrplAmount()), sell.Offers[0].BookQuality);

        BookOffers buy = await client.BookOffers(new BookOffersRequest
        {
            TakerGets = TakerAmount.FromAsset(usd),
            TakerPays = TakerAmount.FromAsset(MptAsset(mpt)),
            LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
        }).Typed();
        Assert.HasCount(1, buy.Offers, "the MPT/USD book holds the one offer");
        Assert.AreEqual(mpt, buy.Offers[0].TakerPays.MPTokenIssuanceID, ignoreCase: true);
        Assert.AreEqual("200", buy.Offers[0].TakerPays.Value);
        Assert.AreEqual(XrplAmount.Parse(usd, "10"), buy.Offers[0].TakerGets.ToXrplAmount());

        BookOffers reverse = await client.BookOffers(new BookOffersRequest
        {
            TakerGets = new TakerAmount { Currency = "XRP" },
            TakerPays = new TakerAmount { MptIssuanceId = mpt },
            LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
        }).Typed();
        Assert.IsEmpty(reverse.Offers ?? new List<BookOffer>(), "nobody buys the MPT for XRP");
    }

    /// <summary>
    /// What <see cref="BookOffersRequest.Validate"/> refuses before sending, the node refuses too:
    /// each request goes out raw, past the check, and comes back with the error it names.
    /// </summary>
    [TestMethod]
    public async Task TestIBookOffers_NodeRefusesWhatValidateRefuses()
    {
        const string issuanceId = "0000012FFD9EE5DA93AC614B4DB94D7E0FCE415CA51BED47";
        (TakerAmount Pays, TakerAmount Gets, string Error)[] cases =
        {
            (new TakerAmount { MptIssuanceId = issuanceId }, new TakerAmount { MptIssuanceId = issuanceId }, "badMarket"),
            (new TakerAmount { MptIssuanceId = issuanceId, Currency = "USD" }, new TakerAmount { Currency = "XRP" }, "invalidParams"),
            (new TakerAmount { MptIssuanceId = issuanceId.Substring(2) }, new TakerAmount { Currency = "XRP" }, "srcCurMalformed"),
            (new TakerAmount { Currency = "XRP" }, new TakerAmount { MptIssuanceId = "Z" + issuanceId.Substring(1) }, "dstAmtMalformed"),
        };

        foreach ((TakerAmount pays, TakerAmount gets, string error) in cases)
        {
            BookOffersRequest request = new BookOffersRequest { TakerPays = pays, TakerGets = gets };
            ValidationException refused = Assert.ThrowsExactly<ValidationException>(request.Validate);
            StringAssert.Contains(refused.Message, error == "invalidParams" ? "Invalid field 'taker_pays'" : error);

            RippledException node = await Assert.ThrowsAsync<RippledException>(
                () => client.GRequest<JsonNode, BookOffersRequest>(request).Typed());
            Assert.AreEqual(error, node.Response?.Error, $"the node's answer to {error}");
        }
    }

    [TestMethod]
    public async Task TestIPayment_XrpToMpt_DeliversThroughTheBook()
    {
        XrplWallet[] wallets = await dex.Wallets(4);
        (XrplWallet issuer, XrplWallet maker, XrplWallet sender, XrplWallet destination) = (wallets[0], wallets[1], wallets[2], wallets[3]);
        string mpt = await IssueMpt(issuer, new[] { maker, destination }, "0");
        await dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = maker.ClassicAddress, Amount = Mpt(mpt, "1000") }, issuer);
        await dex.Submit(new OfferCreate { Account = maker.ClassicAddress, TakerGets = Mpt(mpt, "100"), TakerPays = Drops(1_000_000) }, maker);

        Currency deliver = Mpt(mpt, "30");
        List<List<PathStep>> paths = await FindPaths(sender, destination, deliver, BookCrossingHarness.Xrp);

        TransactionSummary paid = await dex.SubmitWithMeta(
            new Payment
            {
                Account = sender.ClassicAddress,
                Destination = destination.ClassicAddress,
                Amount = deliver,
                SendMax = Drops(300_000),
                Paths = paths,
            },
            sender);

        Assert.AreEqual(XrplAmount.Parse(MptAsset(mpt), "30"), paid.Meta.ActuallyDeliveredAmount.ToXrplAmount(), "delivered_amount");
        Assert.AreEqual(30ul, await MptBalance(destination.ClassicAddress, mpt), "the destination holds the 30 units");
        Assert.AreEqual(970ul, await MptBalance(maker.ClassicAddress, mpt), "the maker's offer gave them");
        AffectedNode makerOffer = paid.Meta.AffectedNodes.Single(n => n.ModifiedNode?.LedgerEntryType == LedgerEntryType.Offer);
        Assert.IsTrue(makerOffer.ModifiedNode.TryGetFinal(out LOOffer remaining));
        Assert.AreEqual(XrplAmount.Parse(BookCrossingHarness.Xrp, "700000"), remaining.TakerPays.ToXrplAmount(), "the offer took 300 000 drops");
    }

    [TestMethod]
    public async Task TestIPayment_IouToMpt_DeliversThroughTheBook()
    {
        XrplWallet[] wallets = await dex.Wallets(4);
        (XrplWallet issuer, XrplWallet maker, XrplWallet sender, XrplWallet destination) = (wallets[0], wallets[1], wallets[2], wallets[3]);
        IssuedCurrency usd = BookCrossingHarness.Iou("USD", issuer);
        await dex.Issuer(issuer);
        await dex.Fund(issuer, sender, usd, "100");
        await dex.Trust(maker, usd);
        string mpt = await IssueMpt(issuer, new[] { maker, destination }, "0");
        await dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = maker.ClassicAddress, Amount = Mpt(mpt, "500") }, issuer);
        await dex.Submit(new OfferCreate { Account = maker.ClassicAddress, TakerGets = Mpt(mpt, "200"), TakerPays = Iou(usd, "20") }, maker);

        Currency deliver = Mpt(mpt, "50");
        List<List<PathStep>> paths = await FindPaths(sender, destination, deliver, usd);

        TransactionSummary paid = await dex.SubmitWithMeta(
            new Payment
            {
                Account = sender.ClassicAddress,
                Destination = destination.ClassicAddress,
                Amount = deliver,
                SendMax = Iou(usd, "5"),
                Paths = paths,
            },
            sender);

        Assert.AreEqual(XrplAmount.Parse(MptAsset(mpt), "50"), paid.Meta.ActuallyDeliveredAmount.ToXrplAmount(), "delivered_amount");
        Assert.AreEqual(50ul, await MptBalance(destination.ClassicAddress, mpt), "the destination holds the 50 units");
        Assert.AreEqual(95m, await IouBalance(sender.ClassicAddress, usd), "the sender paid 5 USD");
        Assert.AreEqual(5m, await IouBalance(maker.ClassicAddress, usd), "the maker received 5 USD");
    }

    /// <summary>
    /// The paths <c>ripple_path_find</c> offers for delivering <paramref name="deliver"/> from
    /// <paramref name="source"/>; null when it finds none or only the default path, which the
    /// payment then takes without a <c>Paths</c> field.
    /// </summary>
    private async Task<List<List<PathStep>>> FindPaths(XrplWallet sender, XrplWallet destination, Currency deliver, IssuedCurrency source)
    {
        RipplePathFindRequest request = new RipplePathFindRequest(sender.ClassicAddress, destination.ClassicAddress, deliver)
        {
            SourceCurrencies = new List<SourceCurrency> { new SourceCurrency { Currency = source.Currency, Issuer = source.Issuer } },
        };
        RipplePathFindResponse found = await client.RipplePathFind(request).Typed();
        PathAlternative alternative = found.Alternatives?.FirstOrDefault();
        TestContext.WriteLine(alternative == null
            ? "ripple_path_find found no alternative; paying along the default path"
            : $"ripple_path_find: {alternative.SourceAmount?.Value} via {alternative.PathsComputed?.Count ?? 0} path(s)");
        return alternative?.PathsComputed is { Count: > 0 } computed ? computed : null;
    }
}
