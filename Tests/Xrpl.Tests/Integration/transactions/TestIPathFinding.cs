using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="PathFinding"/> against the node: each search runs locally, over the ledger's own
/// book index, and through <c>ripple_path_find</c> at the same validated ledger, and every
/// alternative - its cost, what it delivers, its paths in order - must be the node's.
/// </summary>
[TestClass]
public class TestIPathFinding
{
    /// <summary>The stand's <c>[path_search]</c>, which <c>ripple_path_find</c> searches at.</summary>
    private const int NodeSearchLevel = 2;

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

    /// <summary>Runs the search both ways at one ledger and requires the same alternatives.</summary>
    private static async Task<PathFindResult> FindAndCompare(PathFindRequest request)
    {
        LOLedger header = await client.Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }).Typed();
        LedgerIndex at = new LedgerIndex(uint.Parse(((LedgerEntity)header.LedgerEntity).LedgerIndex, CultureInfo.InvariantCulture));

        BookIndex books = await BookIndex.FromLedgerAsync(client, at);
        PathfindingSource source = await PathfindingSource.FromNodeAsync(client, books, at);
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        PathFindResult local = await PathFinding.FindAsync(source, request, rules);

        RipplePathFindRequest rpc = new RipplePathFindRequest(request.SourceAccount, request.DestinationAccount, request.DestinationAmount.ToCurrency())
        {
            SendMax = request.SendMax?.ToCurrency(),
            SourceCurrencies = request.SourceCurrencies.Count == 0
                ? null
                : request.SourceCurrencies.Select(c => new SourceCurrency { Currency = c.Currency, Issuer = c.Issuer }).ToList(),
            Domain = request.DomainId,
            LedgerIndex = at,
        };
        RipplePathFindResponse node = await client.RipplePathFind(rpc).Typed();

        Assert.IsNull(local.Error, "the local search refused the request");
        List<PathAlternative> nodeAlternatives = node.Alternatives ?? new List<PathAlternative>();
        Assert.HasCount(nodeAlternatives.Count, local.Alternatives, $"alternatives: node {Describe(nodeAlternatives)}, local {Describe(local.Alternatives)}");

        foreach (PathAlternative expected in nodeAlternatives)
        {
            XrplAmount expectedSource = expected.SourceAmount.ToXrplAmount();
            PathFindAlternative actual = local.Alternatives.SingleOrDefault(a => XrplAmount.SameAsset(a.SourceAmount.Asset, expectedSource.Asset));
            Assert.IsNotNull(actual, $"no local alternative from {expectedSource.Asset.Currency}/{expectedSource.Asset.Issuer}");
            Assert.AreEqual(expectedSource, actual.SourceAmount, "source_amount");
            if (expected.DestinationAmount != null)
                Assert.AreEqual(expected.DestinationAmount.ToXrplAmount(), actual.DestinationAmount, "destination_amount");

            Assert.AreEqual(Paths(expected.PathsComputed), Paths(actual.PathsComputed), "paths_computed");
        }

        return local;
    }

    private static string Paths(List<List<PathStep>> paths) =>
        string.Join(" | ", (paths ?? new List<List<PathStep>>()).Select(path =>
            string.Join(",", path.Select(step => step.Account ?? $"{step.CurrencyCode}/{step.Issuer}"))));

    private static string Describe(IEnumerable<PathAlternative> alternatives) =>
        "[" + string.Join("; ", alternatives.Select(a => $"{a.SourceAmount.ToXrplAmount()} via {Paths(a.PathsComputed)}")) + "]";

    private static string Describe(IEnumerable<PathFindAlternative> alternatives) =>
        "[" + string.Join("; ", alternatives.Select(a => $"{a.SourceAmount} via {Paths(a.PathsComputed)}")) + "]";

    private static PathFindRequest Request(XrplWallet from, XrplWallet to, XrplAmount deliver, XrplAmount? sendMax = null, params IssuedCurrency[] sources) =>
        new PathFindRequest
        {
            SourceAccount = from.ClassicAddress,
            DestinationAccount = to.ClassicAddress,
            DestinationAmount = deliver,
            SendMax = sendMax,
            SourceCurrencies = sources,
            SearchLevel = NodeSearchLevel,
        };

    [TestMethod]
    public async Task ThroughTheGateway()
    {
        XrplWallet[] w = await dex.Wallets(3);
        XrplWallet gw = w[0], alice = w[1], bob = w[2];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((alice, usd), (bob, usd));
        await dex.Pays((gw, alice, Amount(usd, "70")), (gw, bob, Amount(usd, "50")));

        PathFindResult result = await FindAndCompare(Request(alice, bob, Amount(Iou("USD", bob), "5")));
        Assert.IsNotEmpty(result.Alternatives);
    }

    [TestMethod]
    public async Task RipplingThroughAnIntermediary()
    {
        XrplWallet[] w = await dex.Wallets(3);
        XrplWallet alice = w[0], bob = w[1], carol = w[2];
        await Task.WhenAll(dex.Issuer(alice), dex.Issuer(bob), dex.Issuer(carol));
        await dex.Trust(bob, Iou("USD", alice), "1000");
        await dex.Trust(carol, Iou("USD", bob), "1000");

        PathFindResult result = await FindAndCompare(Request(alice, carol, Amount(Iou("USD", carol), "5")));
        Assert.IsNotEmpty(result.Alternatives);
    }

    [TestMethod]
    public async Task XrpToAnIssuedCurrencyThroughBookAndPool()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet gw = w[0], maker = w[1], creator = w[2], alice = w[3], bob = w[4];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((maker, usd), (creator, usd), (bob, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, creator, Amount(usd, "200")));
        await dex.Offer(maker, Amount(usd, "20"), Drops(10_000_000));
        await dex.CreateAmm(creator, Drops(50_000_000), Amount(usd, "100"), tradingFee: 300);

        PathFindResult exact = await FindAndCompare(Request(alice, bob, Amount(usd, "25"), sources: Xrp));
        Assert.IsNotEmpty(exact.Alternatives);

        // As much as 30 XRP buys, found and then paid.
        PathFindResult all = await FindAndCompare(Request(alice, bob, Amount(usd, "-1"), Drops(30_000_000)));
        PathFindAlternative xrp = all.Alternatives.Single();
        Payment payment = new Payment
        {
            Account = alice.ClassicAddress,
            Destination = bob.ClassicAddress,
            Amount = Amount(usd, "1000").ToCurrency(),
            SendMax = Drops(30_000_000).ToCurrency(),
            Flags = PaymentFlags.tfPartialPayment,
            Paths = xrp.PathsComputed.Count == 0 ? null : xrp.PathsComputed,
        };
        PaymentFlowResult paid = await dex.PayAndCompare(alice, payment);

        // The node's search runs outside a transaction, where the AMM's rounding fixes read as
        // disabled, so its figure and its own payment's can differ in the last digit - here
        // 48.5101515584787 found, 48.51015155847869 delivered. Each side matches the node.
        XrplAmount found = xrp.DestinationAmount.Value;
        XrplAmount delivered = paid.DeliveredAmount.Value;
        XrplAmount gap = XrplAmountMath.Subtract(found, delivered);
        Assert.IsTrue(
            gap.IsZero || XrplAmountMath.Divide(gap.IsNegative ? -gap : gap, delivered, gap.Asset) < XrplAmount.Parse(gap.Asset, "1e-14"),
            $"found {found}, delivered {delivered}");
    }

    [TestMethod]
    public async Task IssuedCurrencyToXrpAndAcrossCurrencies()
    {
        XrplWallet[] w = await dex.Wallets(6);
        XrplWallet gUsd = w[0], gEur = w[1], maker = w[2], alice = w[3], bob = w[4], maker2 = w[5];
        IssuedCurrency usd = Iou("USD", gUsd);
        IssuedCurrency eur = Iou("EUR", gEur);
        await Task.WhenAll(dex.Issuer(gUsd), dex.Issuer(gEur));
        await dex.Trusts((maker, usd), (maker, eur), (alice, usd), (bob, eur), (maker2, usd));
        await dex.Pays((gUsd, alice, Amount(usd, "100")), (gEur, maker, Amount(eur, "100")));
        await dex.Offer(maker, Drops(40_000_000), Amount(usd, "20"));
        await dex.Offer(maker, Amount(eur, "30"), Amount(usd, "35"));

        await FindAndCompare(Request(alice, bob, Drops(10_000_000)));
        PathFindResult cross = await FindAndCompare(Request(alice, bob, Amount(eur, "10")));
        Assert.IsNotEmpty(cross.Alternatives);
    }

    [TestMethod]
    public async Task ConsumeAllAcrossTwoRipplingPaths()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet alice = w[0], bob = w[1], carol = w[2], dan = w[3], edward = w[4];
        await Task.WhenAll(w.Select(x => dex.Issuer(x)));
        await Task.WhenAll(
            dex.Trust(bob, Iou("USD", alice), "10"),
            dex.Trust(carol, Iou("USD", bob), "10"),
            dex.Trust(dan, Iou("USD", alice), "100"));
        await Task.WhenAll(dex.Trust(edward, Iou("USD", carol), "10"));
        await dex.Trust(edward, Iou("USD", dan), "100");

        PathFindResult result = await FindAndCompare(Request(alice, edward, Amount(Iou("USD", edward), "-1")));
        Assert.IsNotEmpty(result.Alternatives);
    }

    [TestMethod]
    public async Task SourceCurrencyIssuerSelection()
    {
        XrplWallet[] w = await dex.Wallets(3);
        XrplWallet gw = w[0], alice = w[1], bob = w[2];
        IssuedCurrency usd = Iou("USD", gw);
        await Task.WhenAll(dex.Issuer(gw), dex.Issuer(alice));
        await dex.Trusts((alice, usd), (bob, usd));
        await dex.Trust(bob, Iou("USD", alice), "700");
        await dex.Pays((gw, alice, Amount(usd, "70")), (gw, bob, Amount(usd, "50")));

        PathFindResult result = await FindAndCompare(Request(alice, bob, Amount(Iou("USD", bob), "-1"), Amount(Iou("USD", alice), "100"), usd));
        Assert.HasCount(1, result.Alternatives);
    }

    [TestMethod]
    public async Task EverySourceAssetTheAccountHolds()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], maker = w[1], alice = w[2], bob = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((maker, usd), (alice, usd), (bob, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, alice, Amount(usd, "40")));
        await dex.Offer(maker, Amount(usd, "30"), Drops(15_000_000));

        // No source currencies: XRP through the book, and USD directly.
        PathFindResult result = await FindAndCompare(Request(alice, bob, Amount(usd, "12")));
        Assert.HasCount(2, result.Alternatives);
    }

    [TestMethod]
    public async Task ThroughADomainBook()
    {
        XrplWallet[] w = await dex.Wallets(6);
        XrplWallet gw = w[0], issuer = w[1], alice = w[2], bob = w[3], maker = w[4], openMaker = w[5];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        string domain = await dex.Domain(issuer, (issuer, "KYC"));
        foreach (XrplWallet member in new[] { alice, bob, maker })
            await dex.Credential(issuer, member, "KYC");
        await dex.Trusts((maker, usd), (openMaker, usd), (bob, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, openMaker, Amount(usd, "100")));
        await dex.Offer(openMaker, Amount(usd, "20"), Drops(8_000_000));
        await dex.Offer(maker, Amount(usd, "20"), Drops(10_000_000), domainId: domain);

        PathFindRequest request = Request(alice, bob, Amount(usd, "5"), sources: Xrp);
        PathFindResult inDomain = await FindAndCompare(new PathFindRequest
        {
            SourceAccount = request.SourceAccount,
            DestinationAccount = request.DestinationAccount,
            DestinationAmount = request.DestinationAmount,
            SourceCurrencies = request.SourceCurrencies,
            DomainId = domain,
            SearchLevel = NodeSearchLevel,
        });
        Assert.IsNotEmpty(inDomain.Alternatives);
    }

    [TestMethod]
    public async Task FoundPathsPayAsFound()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], maker = w[1], alice = w[2], bob = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw, transferRate: 1_002_000_000);
        await dex.Trusts((maker, usd), (bob, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")));
        await dex.Offer(maker, Amount(usd, "30"), Drops(15_000_000));

        PathFindResult found = await FindAndCompare(Request(alice, bob, Amount(usd, "10"), sources: Xrp));
        PathFindAlternative xrp = found.Alternatives.Single();

        // The payment built from the local search delivers, and costs what the search said.
        Payment payment = new Payment
        {
            Account = alice.ClassicAddress,
            Destination = bob.ClassicAddress,
            Amount = Amount(usd, "10").ToCurrency(),
            SendMax = xrp.SourceAmount.ToCurrency(),
            Paths = xrp.PathsComputed.Count == 0 ? null : xrp.PathsComputed,
        };
        PaymentFlowResult paid = await dex.PayAndCompare(alice, payment);
        Assert.AreEqual("tesSUCCESS", paid.EngineResult);
        Assert.AreEqual(xrp.SourceAmount, paid.Paid);
    }
}
