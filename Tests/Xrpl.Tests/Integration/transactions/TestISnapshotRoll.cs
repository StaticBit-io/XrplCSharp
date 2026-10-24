using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <c>SnapshotAfter</c> against the node (issue 301): a chain of payments and offers is evaluated
/// offline, each against the snapshot the last one left, and submitted to the node one by one.
/// After each, the state is read back with <see cref="DexSnapshot.FromNodeAsync(IXrplClient, Payment, DexSnapshotOptions, System.Threading.CancellationToken)"/>
/// and compared with the rolled snapshot: every account's balance, owner count and sequence,
/// every trust line, and both books offer by offer, in order, with their qualities.
/// </summary>
[TestClass]
public class TestISnapshotRoll
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

    private static Payment Pay(XrplWallet from, string to, XrplAmount amount, XrplAmount? sendMax = null) =>
        new Payment { Account = from.ClassicAddress, Destination = to, Amount = amount.ToCurrency(), SendMax = sendMax?.ToCurrency() };

    private static OfferCreate Offer(XrplWallet maker, XrplAmount takerPays, XrplAmount takerGets, OfferCreateFlags flags = 0) =>
        new OfferCreate { Account = maker.ClassicAddress, TakerPays = takerPays.ToCurrency(), TakerGets = takerGets.ToCurrency(), Flags = flags };

    [TestMethod]
    public async Task AChainRolledOfflineMatchesTheNode()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet gw = w[0], m1 = w[1], m2 = w[2], a = w[3], b = w[4];
        string c = XrplWallet.Generate().ClassicAddress;
        IssuedCurrency usd = Iou("USD", gw);

        await dex.Issuer(gw);
        await dex.Trusts((m1, usd), (m2, usd), (a, usd), (b, usd));
        await dex.Pays((gw, m1, Amount(usd, "1000")), (gw, m2, Amount(usd, "1000")));

        // m1 sells USD from 1 XRP up, m2 buys it from 0.9 XRP down: the books do not cross.
        await dex.Offer(m1, Amount(usd, "100"), Drops(100_000_000));
        await dex.Offer(m1, Amount(usd, "100"), Drops(110_000_000));
        await dex.Offer(m1, Amount(usd, "100"), Drops(120_000_000));
        await dex.Offer(m2, Drops(90_000_000), Amount(usd, "100"));
        await dex.Offer(m2, Drops(80_000_000), Amount(usd, "100"));

        (string Name, XrplWallet Signer, ITransactionRequest Transaction, string Expected)[] chain =
        {
            ("payment through the book", a, Pay(a, b.ClassicAddress, Amount(usd, "150"), Drops(200_000_000)), "tesSUCCESS"),
            ("offer crossed in part, the rest placed", a, Offer(a, Amount(usd, "100"), Drops(115_000_000)), "tesSUCCESS"),
            ("sale through the placed offer", m1, Offer(m1, Drops(1), Amount(usd, "70"), OfferCreateFlags.tfSell), "tesSUCCESS"),
            ("payment along the lines", b, Pay(b, a.ClassicAddress, Amount(usd, "30")), "tesSUCCESS"),
            ("fill or kill, killed", b, Offer(b, Drops(1_000_000_000), Amount(usd, "10"), OfferCreateFlags.tfFillOrKill), "tecKILLED"),
            ("account created", a, Pay(a, c, Drops(20_000_000)), "tesSUCCESS"),
            ("offer placed without crossing", a, Offer(a, Amount(usd, "10"), Drops(5_000_000)), "tesSUCCESS"),
        };

        await RunAgainstNode(chain, a, b, c, usd, new[] { gw, m1, m2, a, b }.Select(x => x.ClassicAddress).Append(c).ToArray(), new HashSet<string> { c });
    }

    [TestMethod]
    public async Task AChainThroughAPoolRolledOfflineMatchesTheNode()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], m1 = w[1], a = w[2], b = w[3];
        IssuedCurrency usd = Iou("USD", gw);

        await dex.Issuer(gw);
        await dex.Trusts((m1, usd), (a, usd), (b, usd));
        await dex.Pays((gw, m1, Amount(usd, "3000")));
        await dex.CreateAmm(m1, Drops(100_000_000), Amount(usd, "100"), 500);
        await dex.Offer(m1, Amount(usd, "10"), Drops(10_100_000));
        await dex.Offer(m1, Amount(usd, "10"), Drops(11_000_000));
        await dex.Offer(m1, Drops(9_000_000), Amount(usd, "10"));

        (string Name, XrplWallet Signer, ITransactionRequest Transaction, string Expected)[] chain =
        {
            ("payment through pool and book", a, Pay(a, b.ClassicAddress, Amount(usd, "8"), Drops(20_000_000)), "tesSUCCESS"),
            ("offer crossing pool and book", a, Offer(a, Amount(usd, "5"), Drops(6_000_000)), "tesSUCCESS"),
            ("sale into pool and bid", b, Offer(b, Drops(1), Amount(usd, "4"), OfferCreateFlags.tfSell), "tesSUCCESS"),
            ("USD for XRP through the pool", b, Pay(b, a.ClassicAddress, Drops(500_000), Amount(usd, "1")), "tesSUCCESS"),
        };

        string unused = XrplWallet.Generate().ClassicAddress;
        DexSnapshot final = await RunAgainstNode(chain, a, b, unused, usd, new[] { gw, m1, a, b }.Select(x => x.ClassicAddress).ToArray(), new HashSet<string>());

        DexAmmPool pool = final.Pools.Single();
        Assert.AreNotEqual(Drops(100_000_000), pool.Balance.Kind == AmountKind.Xrp ? pool.Balance : pool.Balance2, "the chain moved the pool");
    }

    /// <summary>Evaluates each transaction against the snapshot the last one left, submits it, and compares the snapshot with the node.</summary>
    private static async Task<DexSnapshot> RunAgainstNode(
        (string Name, XrplWallet Signer, ITransactionRequest Transaction, string Expected)[] chain,
        XrplWallet a,
        XrplWallet b,
        string c,
        IssuedCurrency usd,
        string[] accounts,
        HashSet<string> created)
    {
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        DexSnapshot snapshot = await ReadBack(a, b, c, usd);
        foreach ((string name, XrplWallet signer, ITransactionRequest transaction, string expected) in chain)
        {
            ITransactionRequest filled = transaction is Payment payment ? await client.Autofill(payment) : await client.Autofill((OfferCreate)transaction);
            (string predicted, DexSnapshot after) = filled is Payment p
                ? Predict(PaymentFlow.Evaluate(snapshot, p, rules))
                : Predict(OfferCreateCrossing.Cross(snapshot, (OfferCreate)filled, rules));
            Assert.AreEqual(expected, predicted, $"{name}: predicted");

            string actual = await Submit(filled, signer);
            Assert.AreEqual(expected, actual, $"{name}: on the node");

            AssertMatchesNode(after, await ReadBack(a, b, c, usd), accounts, created, usd, name);
            snapshot = after;
        }

        return snapshot;
    }

    private static (string, DexSnapshot) Predict(PaymentFlowResult result) => (result.EngineResult, result.SnapshotAfter);

    private static (string, DexSnapshot) Predict(OfferCrossingResult result) => (result.EngineResult, result.SnapshotAfter);

    private static async Task<string> Submit(ITransactionRequest transaction, XrplWallet wallet)
    {
        try
        {
            TransactionSummary result = transaction is Payment payment
                ? await client.SubmitAndWait(payment, wallet, false)
                : await client.SubmitAndWait((OfferCreate)transaction, wallet, false);
            return result.Meta.TransactionResult;
        }
        catch (TransactionFailedException failed) when (failed.EngineResult?.StartsWith("tec", StringComparison.Ordinal) == true)
        {
            return failed.EngineResult;
        }
    }

    /// <summary>
    /// What the chain reaches, read at one ledger: both books with their owners, the two takers
    /// and their lines, the issuer, and the account the chain creates once it exists.
    /// </summary>
    private static async Task<DexSnapshot> ReadBack(XrplWallet a, XrplWallet b, string c, IssuedCurrency usd)
    {
        DexSnapshot first = await DexSnapshot.FromNodeAsync(client, Pay(a, b.ClassicAddress, Amount(usd, "1"), Drops(1_000_000)));
        DexSnapshotOptions sameLedger = new DexSnapshotOptions { Ledger = new LedgerIndex(first.LedgerSequence) };
        return Merge(
            first,
            await DexSnapshot.FromNodeAsync(client, Pay(a, c, Drops(1_000_000)), sameLedger),
            await DexSnapshot.FromNodeAsync(client, a.ClassicAddress, usd, Xrp, sameLedger),
            await DexSnapshot.FromNodeAsync(client, a.ClassicAddress, Xrp, usd, sameLedger));
    }

    /// <summary>Several reads of one ledger as one snapshot: each entry once, each book in its read order.</summary>
    private static DexSnapshot Merge(params DexSnapshot[] reads)
    {
        Assert.IsTrue(reads.All(r => r.LedgerSequence == reads[0].LedgerSequence), "the reads are of one ledger");
        return new DexSnapshot
        {
            ParentCloseTime = reads[0].ParentCloseTime,
            LedgerSequence = reads[0].LedgerSequence,
            ReserveBase = reads[0].ReserveBase,
            ReserveIncrement = reads[0].ReserveIncrement,
            Accounts = reads.SelectMany(r => r.Accounts).GroupBy(x => x.Address).Select(g => g.First()).ToList(),
            TrustLines = reads.SelectMany(r => r.TrustLines)
                .GroupBy(l => LineKey.Of(l.Account, l.Balance.Asset.Issuer, l.Balance.Asset.Currency))
                .Select(g => g.First())
                .ToList(),
            Offers = reads.SelectMany(r => r.Offers).GroupBy(o => o.Index).Select(g => g.First()).ToList(),
            Pools = reads.SelectMany(r => r.Pools).GroupBy(p => p.Account).Select(g => g.First()).ToList(),
            PartialBooks = reads.SelectMany(r => r.PartialBooks).ToList(),
        };
    }

    private static void AssertMatchesNode(
        DexSnapshot rolled, DexSnapshot node, IEnumerable<string> accounts, ISet<string> created, IssuedCurrency usd, string step)
    {
        Assert.AreEqual(0, node.PartialBooks.Count, $"{step}: the books are read whole");
        DexIndex expected = new DexIndex(node);
        DexIndex actual = DexIndex.Of(rolled);

        foreach (string account in accounts)
        {
            bool onNode = expected.Accounts.TryGetValue(account, out DexAccount fromNode);
            bool inRoll = actual.Accounts.TryGetValue(account, out DexAccount fromRoll);
            Assert.AreEqual(onNode, inRoll, $"{step}: {account} exists");
            if (!onNode)
                continue;

            Assert.AreEqual(fromNode.Balance, fromRoll.Balance, $"{step}: {account} Balance");
            Assert.AreEqual(fromNode.OwnerCount, fromRoll.OwnerCount, $"{step}: {account} OwnerCount");
            // An account the chain created has the sequence of the ledger it landed in, which offline
            // is only bounded from below: each step here closes ledgers of its own.
            if (created.Contains(account))
                Assert.IsTrue(fromRoll.Sequence <= fromNode.Sequence, $"{step}: {account} Sequence {fromRoll.Sequence} at most the node's {fromNode.Sequence}");
            else
                Assert.AreEqual(fromNode.Sequence, fromRoll.Sequence, $"{step}: {account} Sequence");
        }

        HashSet<string> chain = new HashSet<string>(accounts, StringComparer.Ordinal);
        string Lines(DexIndex index) => string.Join("\n", index.Lines
            .Where(e => chain.Contains(e.Key.First) && chain.Contains(e.Key.Second))
            .Select(e => $"{e.Key} {Side(e.Value.First)} | {Side(e.Value.Second)} | {e.Value.DeepFrozen} {e.Value.Balance}")
            .OrderBy(x => x, StringComparer.Ordinal));
        Assert.AreEqual(Lines(expected), Lines(actual), $"{step}: trust lines");

        string Pools(DexIndex index) => string.Join("\n", index.Pools
            .Select(pool => $"{pool.Account} {pool.Balance} {pool.Balance2}")
            .OrderBy(x => x, StringComparer.Ordinal));
        Assert.AreEqual(Pools(expected), Pools(actual), $"{step}: pools");

        foreach (BookKey book in new[] { BookKey.Of(usd, Xrp), BookKey.Of(Xrp, usd) })
        {
            string Book(DexIndex index) => string.Join("\n", index.Book(book)
                .Select(o => $"{o.Index} {o.Account} {o.TakerPays} {o.TakerGets} {o.Quality.Value}"));
            Assert.AreEqual(Book(expected), Book(actual), $"{step}: book {book}");
        }
    }

    private static string Side(LineSide side) =>
        $"{side.Limit} {side.QualityIn} {side.QualityOut} {side.NoRipple} {side.Freeze} {side.Auth} {side.Reserve}";
}
