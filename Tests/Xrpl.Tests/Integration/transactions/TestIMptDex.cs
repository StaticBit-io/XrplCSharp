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
/// currency, crossed and paid through, each predicted by <see cref="OfferCreateCrossing"/> or
/// <see cref="PaymentFlow"/> from a snapshot read just before and compared with what the node
/// did - offers, balances, OutstandingAmount and the MPTokens created - to the last unit. Also
/// <c>book_offers</c> reading an MPT book.
/// </summary>
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
        bool standalone = IntegrationTestConfig.IsStandalone(nodeType);

        // As TestIAMMMpt: on a standalone stand MPTokensV2 is a [features] Rules preset the
        // on-ledger guard and the feature command cannot see, so the tests run there, and the
        // prediction is told the amendment is on.
        dex = new BookCrossingHarness(client, nodeType) { AdjustRules = rules => standalone ? rules.WithMPTokensV2() : rules };
        mptokensV2Usable = standalone || await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.MPTokensV2);
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

    private static XrplAmount Units(string issuanceId, long units) => XrplAmount.Parse(MptAsset(issuanceId), units.ToString(CultureInfo.InvariantCulture));

    private static Currency Mpt(string issuanceId, string value) => new Currency { Value = value, MPTokenIssuanceID = issuanceId };

    private static XrplAmount Drops(long drops) => BookCrossingHarness.Drops(drops);

    /// <summary>
    /// An MPT that can be traded and transferred - and locked, when asked - with its transfer fee
    /// and maximum; each holder is authorized and given <paramref name="units"/>.
    /// </summary>
    private static async Task<string> IssueMpt(
        XrplWallet issuer,
        IEnumerable<XrplWallet> holders,
        string units,
        ushort? transferFee = null,
        string maximum = null,
        bool canLock = false)
    {
        MPTokenIssuanceCreateFlags flags = MPTokenIssuanceCreateFlags.tfMPTCanTrade | MPTokenIssuanceCreateFlags.tfMPTCanTransfer;
        if (canLock)
            flags |= MPTokenIssuanceCreateFlags.tfMPTCanLock;

        TransactionSummary created = await dex.SubmitWithMeta(
            new MPTokenIssuanceCreate { Account = issuer.ClassicAddress, Flags = flags, TransferFee = transferFee, MaximumAmount = maximum },
            issuer);
        string issuanceId = created.Meta.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID in the metadata");

        foreach (XrplWallet holder in holders)
        {
            await Hold(holder, issuanceId);
            if (units != "0")
                await dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = holder.ClassicAddress, Amount = Mpt(issuanceId, units) }, issuer);
        }

        return issuanceId;
    }

    private static Task Hold(XrplWallet holder, string issuanceId) =>
        dex.Submit(new MPTokenAuthorize { Account = holder.ClassicAddress, MPTokenIssuanceID = issuanceId }, holder);

    private static Task Give(XrplWallet issuer, XrplWallet holder, string issuanceId, string units) =>
        dex.Submit(new Payment { Account = issuer.ClassicAddress, Destination = holder.ClassicAddress, Amount = Mpt(issuanceId, units) }, issuer);

    // ---- offer crossing ----

    [TestMethod]
    public async Task TestIOfferCreate_MptForXrp_CrossesTheRestingOffer()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { maker, taker }, "0");
        await Give(issuer, maker, mpt, "1000");

        // The maker sells 100 units at 10 000 drops each; the taker buys 40 of them.
        await dex.Offer(maker, Units(mpt, 100), Drops(1_000_000));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 40), Drops(400_000));

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Units(mpt, 40), result.Received);
        Assert.IsNull(result.PlacedTakerPays, "the taker's offer is filled");
    }

    [TestMethod]
    public async Task TestIOfferCreate_MptForIou_ConsumesTheRestingOfferAndPlacesTheRest()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        IssuedCurrency usd = BookCrossingHarness.Iou("USD", issuer);
        await dex.Issuer(issuer);
        await dex.Fund(issuer, taker, usd, "100");
        await dex.Trust(maker, usd);
        string mpt = await IssueMpt(issuer, new[] { maker, taker }, "0");
        await Give(issuer, maker, mpt, "50");

        // The maker sells all 50 units for 5 USD; the taker offers more than is on the book.
        await dex.Offer(maker, Units(mpt, 50), BookCrossingHarness.Amount(usd, "5"));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 80), BookCrossingHarness.Amount(usd, "8"));

        Assert.AreEqual(Units(mpt, 30), result.PlacedTakerPays, "the remainder asks the 30 units left");
        Assert.AreEqual(BookCrossingHarness.Amount(usd, "3"), result.PlacedTakerGets);
    }

    /// <summary>A taker without an MPToken gets one as the MPT arrives; one more object it owns.</summary>
    [TestMethod]
    public async Task TestIOfferCreate_CreatesTheTakersMpToken()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { maker }, "500");

        await dex.Offer(maker, Units(mpt, 100), Drops(1_000_000));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 25), Drops(250_000));

        Assert.HasCount(1, result.MpTokens, "the taker's MPToken");
        Assert.AreEqual(taker.ClassicAddress, result.MpTokens[0].Account);
    }

    /// <summary>
    /// A 1% transfer fee, paid by the offer's owner on what it gives and rounded up: the
    /// issuer's OutstandingAmount drops by the fee.
    /// </summary>
    [TestMethod]
    public async Task TestIOfferCreate_TransferFeeRoundsUpOnTheOwner()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { maker, taker }, "0", transferFee: 1000);
        await Give(issuer, maker, mpt, "1000");

        await dex.Offer(maker, Units(mpt, 100), Drops(1_000_000));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 13), Drops(130_000));

        XrplAmount makerGave = result.BalanceChanges.Single(c => c.Account == maker.ClassicAddress && c.Change.Kind == AmountKind.Mpt).Change;
        Assert.AreEqual(Units(mpt, -14), makerGave, "13.13 units, rounded up");
    }

    /// <summary>The selling side read by walking the book's directories, which finds the MPT book's tagged key.</summary>
    [TestMethod]
    public async Task TestIOfferCreate_WalksTheMptBook()
    {
        XrplWallet[] wallets = await dex.Wallets(4);
        (XrplWallet issuer, XrplWallet maker, XrplWallet other, XrplWallet taker) = (wallets[0], wallets[1], wallets[2], wallets[3]);
        string mpt = await IssueMpt(issuer, new[] { maker, other, taker }, "0");
        await Give(issuer, maker, mpt, "300");
        await Give(issuer, other, mpt, "300");

        await dex.Offer(maker, Units(mpt, 100), Drops(1_100_000));
        await dex.Offer(other, Units(mpt, 100), Drops(1_000_000));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 150), Drops(1_650_000), options: new DexSnapshotOptions { BookDepth = 10 });

        Assert.HasCount(2, result.Offers, "both offers are crossed, the cheaper first");
    }

    /// <summary>
    /// The issuer's own offer gives no more than it may still issue: OutstandingAmount stops at
    /// MaximumAmount, the offer is left unfunded and removed, and the rest of the taker's offer
    /// is placed.
    /// </summary>
    [TestMethod]
    public async Task TestIOfferCreate_IssuersOwnOfferStopsAtTheMaximum()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet holder, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { holder, taker }, "0", maximum: "1000");
        await Give(issuer, holder, mpt, "950");

        await dex.Offer(issuer, Units(mpt, 100), Drops(1_000_000));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 80), Drops(800_000));

        Assert.AreEqual(Units(mpt, 50), result.Received, "the 50 left to issue");
        Assert.AreEqual(Units(mpt, 30), result.PlacedTakerPays);
    }

    /// <summary>An MPT/XRP pool and a resting offer: the taker's crossing takes from both, the pool's MPToken moving with it.</summary>
    [TestMethod]
    public async Task TestIOfferCreate_AgainstAnMptPoolAndTheBook()
    {
        XrplWallet[] wallets = await dex.Wallets(4);
        (XrplWallet issuer, XrplWallet creator, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2], wallets[3]);
        string mpt = await IssueMpt(issuer, new[] { creator, maker, taker }, "0", transferFee: 200);
        await Give(issuer, creator, mpt, "10000");
        await Give(issuer, maker, mpt, "1000");

        await dex.CreateAmm(creator, Units(mpt, 5000), Drops(50_000_000), tradingFee: 300);
        await dex.Offer(maker, Units(mpt, 200), Drops(2_100_000));
        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 300), Drops(3_300_000));

        Assert.IsNotEmpty(result.Pools, "the pool is traded against");
    }

    [TestMethod]
    public async Task TestIOfferCreate_LockedIssuanceIsRefused()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet maker, XrplWallet taker) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { maker, taker }, "0", canLock: true);
        await Give(issuer, maker, mpt, "100");
        await dex.Offer(maker, Units(mpt, 100), Drops(1_000_000));
        await dex.Submit(new MPTokenIssuanceSet { Account = issuer.ClassicAddress, MPTokenIssuanceID = mpt, Flags = MPTokenIssuanceSetFlags.tfMPTLock }, issuer);

        OfferCrossingResult result = await dex.CrossAndCompare(taker, Units(mpt, 10), Drops(100_000));
        Assert.AreEqual("tecLOCKED", result.EngineResult);
    }

    // ---- book_offers ----

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
        await dex.Offer(maker, Units(mpt, 100), Drops(2_000_000));
        await dex.Offer(other, Units(mpt, 100), Drops(1_000_000));
        await dex.Offer(maker, BookCrossingHarness.Amount(usd, "10"), Units(mpt, 200));

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

    // ---- payments ----

    [TestMethod]
    public async Task TestIPayment_XrpToMpt_DeliversThroughTheBook()
    {
        XrplWallet[] wallets = await dex.Wallets(4);
        (XrplWallet issuer, XrplWallet maker, XrplWallet sender, XrplWallet destination) = (wallets[0], wallets[1], wallets[2], wallets[3]);
        string mpt = await IssueMpt(issuer, new[] { maker, destination }, "0");
        await Give(issuer, maker, mpt, "1000");
        await dex.Offer(maker, Units(mpt, 100), Drops(1_000_000));

        Currency deliver = Mpt(mpt, "30");
        PaymentFlowResult result = await dex.PayAndCompare(
            sender,
            new Payment
            {
                Account = sender.ClassicAddress,
                Destination = destination.ClassicAddress,
                Amount = deliver,
                SendMax = Drops(300_000).ToCurrency(),
                Paths = await FindPaths(sender, destination, deliver, BookCrossingHarness.Xrp),
            });

        Assert.AreEqual(Units(mpt, 30), result.DeliveredAmount);
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
        await Give(issuer, maker, mpt, "500");
        await dex.Offer(maker, Units(mpt, 200), BookCrossingHarness.Amount(usd, "20"));

        Currency deliver = Mpt(mpt, "50");
        PaymentFlowResult result = await dex.PayAndCompare(
            sender,
            new Payment
            {
                Account = sender.ClassicAddress,
                Destination = destination.ClassicAddress,
                Amount = deliver,
                SendMax = BookCrossingHarness.Amount(usd, "5").ToCurrency(),
                Paths = await FindPaths(sender, destination, deliver, usd),
            });

        Assert.AreEqual(Units(mpt, 50), result.DeliveredAmount);
    }

    /// <summary>XRP to an MPT with a 1% fee: the issuer issues the amount and takes it plus the fee, rounded up, from the book.</summary>
    [TestMethod]
    public async Task TestIPayment_XrpToMpt_TransferFee()
    {
        XrplWallet[] wallets = await dex.Wallets(4);
        (XrplWallet issuer, XrplWallet maker, XrplWallet sender, XrplWallet destination) = (wallets[0], wallets[1], wallets[2], wallets[3]);
        string mpt = await IssueMpt(issuer, new[] { maker, destination }, "0", transferFee: 1000);
        await Give(issuer, maker, mpt, "1000");
        await dex.Offer(maker, Units(mpt, 100), Drops(1_000_000));

        PaymentFlowResult result = await dex.PayAndCompare(
            sender,
            new Payment
            {
                Account = sender.ClassicAddress,
                Destination = destination.ClassicAddress,
                Amount = Mpt(mpt, "30"),
                SendMax = Drops(400_000).ToCurrency(),
            });

        Assert.AreEqual(Drops(310_000), result.Paid, "30.3 units rounded up to 31, at 10 000 drops each");
    }

    /// <summary>Between holders the engine charges the 1% fee on top: SendMax must allow it.</summary>
    [TestMethod]
    public async Task TestIPayment_BetweenHolders_TransferFee()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet sender, XrplWallet destination) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { sender, destination }, "0", transferFee: 1000);
        await Give(issuer, sender, mpt, "1000");

        PaymentFlowResult paid = await dex.PayAndCompare(
            sender,
            new Payment { Account = sender.ClassicAddress, Destination = destination.ClassicAddress, Amount = Mpt(mpt, "100"), SendMax = Mpt(mpt, "101") });
        Assert.AreEqual(Units(mpt, 101), paid.Paid);

        PaymentFlowResult partial = await dex.PayAndCompare(
            sender,
            new Payment { Account = sender.ClassicAddress, Destination = destination.ClassicAddress, Amount = Mpt(mpt, "100") });
        Assert.AreEqual("tecPATH_PARTIAL", partial.EngineResult, "SendMax is the amount when absent");
    }

    [TestMethod]
    public async Task TestIPayment_LockedSender()
    {
        XrplWallet[] wallets = await dex.Wallets(3);
        (XrplWallet issuer, XrplWallet sender, XrplWallet destination) = (wallets[0], wallets[1], wallets[2]);
        string mpt = await IssueMpt(issuer, new[] { sender, destination }, "0", canLock: true);
        await Give(issuer, sender, mpt, "100");
        await dex.Submit(
            new MPTokenIssuanceSet { Account = issuer.ClassicAddress, MPTokenIssuanceID = mpt, Holder = sender.ClassicAddress, Flags = MPTokenIssuanceSetFlags.tfMPTLock },
            issuer);

        PaymentFlowResult result = await dex.PayAndCompare(
            sender,
            new Payment { Account = sender.ClassicAddress, Destination = destination.ClassicAddress, Amount = Mpt(mpt, "10") });
        Assert.AreEqual("tecPATH_DRY", result.EngineResult, "the strand's terLOCKED, charged");
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
