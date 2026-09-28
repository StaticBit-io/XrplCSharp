using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.Amounts;
using Xrpl.Models.Common;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// <see cref="PathFinding"/> against the path searches rippled's own <c>Path_test.cpp</c> pins,
/// rebuilt as snapshots at the search level those tests use (7). The accounts are jtx's: the
/// address derived from the name, so candidates sort by account id as they do there.
/// </summary>
[TestClass]
public class TestUPathFinding
{
    private const int PathTestSearchLevel = 7;

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static readonly Dictionary<string, string> Addresses = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>jtx's <c>Account(name)</c>: the secp256k1 account of the seed <c>sha512(name)</c> truncated to 16 bytes.</summary>
    private static string A(string name)
    {
        if (!Addresses.TryGetValue(name, out string address))
        {
            byte[] seed = SHA512.HashData(Encoding.UTF8.GetBytes(name)).Take(16).ToArray();
            Addresses[name] = address = XrplWallet.FromSeed(XrplCodec.EncodeSeed(seed, "secp256k1")).ClassicAddress;
        }

        return address;
    }

    private static IssuedCurrency Iou(string currency, string issuer) => new IssuedCurrency { Currency = currency, Issuer = A(issuer) };

    private static XrplAmount Amount(string currency, string issuer, string value) => XrplAmount.Parse(Iou(currency, issuer), value);

    private static XrplAmount Drops(long drops) => XrplAmount.Parse(Xrp, drops.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A jtx ledger: accounts funded with DefaultRipple, trust lines in the order they were created.</summary>
    private sealed class Ledger
    {
        private readonly List<DexAccount> _accounts = new List<DexAccount>();
        private readonly List<DexTrustLine> _lines = new List<DexTrustLine>();
        private readonly List<DexOffer> _offers = new List<DexOffer>();

        internal Ledger(params string[] names)
        {
            foreach (string name in names)
                _accounts.Add(new DexAccount { Address = A(name), Balance = 10_000_000_000, DefaultRipple = true });
        }

        internal Ledger Rate(string name, uint rate)
        {
            int i = _accounts.FindIndex(a => a.Address == A(name));
            DexAccount old = _accounts[i];
            _accounts[i] = new DexAccount { Address = old.Address, Balance = old.Balance, DefaultRipple = true, TransferRate = rate };
            return this;
        }

        /// <summary><c>env.trust(issuer["USD"](limit), holder)</c>: a new line, or the holder's side of an existing one.</summary>
        internal Ledger Trust(string holder, string issuer, string limit, string currency = "USD")
        {
            int i = Find(holder, issuer, currency);
            if (i < 0)
            {
                _lines.Add(new DexTrustLine
                {
                    Account = A(holder),
                    Balance = Amount(currency, issuer, "0"),
                    Limit = Amount(currency, issuer, limit),
                    Reserve = true,
                });
                return this;
            }

            DexTrustLine line = _lines[i];
            bool fromHolder = line.Account == A(holder);
            _lines[i] = fromHolder
                ? Copy(line, limit: Amount(currency, issuer, limit))
                : Copy(line, peerLimit: XrplAmount.Parse(line.Balance.Asset, limit));
            return this;
        }

        /// <summary>What <paramref name="holder"/> holds of <paramref name="issuer"/>'s currency once the setup payments are done.</summary>
        internal Ledger Holds(string holder, string issuer, string value, string currency = "USD")
        {
            int i = Find(holder, issuer, currency);
            DexTrustLine line = _lines[i];
            XrplAmount balance = line.Account == A(holder)
                ? Amount(currency, issuer, value)
                : -XrplAmount.Parse(line.Balance.Asset, value);
            _lines[i] = Copy(line, balance: balance);
            return this;
        }

        internal Ledger Offer(string owner, XrplAmount takerPays, XrplAmount takerGets)
        {
            _offers.Add(new DexOffer
            {
                Index = (_offers.Count + 1).ToString("X64", System.Globalization.CultureInfo.InvariantCulture),
                Account = A(owner),
                TakerPays = takerPays,
                TakerGets = takerGets,
                Quality = XrplQuality.FromAmounts(takerPays, takerGets),
            });
            return this;
        }

        private int Find(string a, string b, string currency) =>
            _lines.FindIndex(l => l.Balance.Asset.Currency == currency &&
                                  ((l.Account == A(a) && l.Balance.Asset.Issuer == A(b)) || (l.Account == A(b) && l.Balance.Asset.Issuer == A(a))));

        private static DexTrustLine Copy(DexTrustLine line, XrplAmount? limit = null, XrplAmount? peerLimit = null, XrplAmount? balance = null) =>
            new DexTrustLine
            {
                Account = line.Account,
                Balance = balance ?? line.Balance,
                Limit = limit ?? line.Limit,
                PeerLimit = peerLimit ?? line.PeerLimit,
                Reserve = line.Reserve,
                PeerReserve = line.PeerReserve || peerLimit != null,
            };

        internal DexSnapshot Snapshot() => new DexSnapshot
        {
            ParentCloseTime = 800_000_000,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = _accounts.ToList(),
            TrustLines = _lines.ToList(),
            Offers = _offers.ToList(),
        };
    }

    private static Task<PathFindResult> Find(Ledger ledger, string from, string to, XrplAmount deliver, XrplAmount? sendMax = null) =>
        PathFinding.FindAsync(
            PathfindingSource.FromSnapshot(ledger.Snapshot()),
            new PathFindRequest
            {
                SourceAccount = A(from),
                DestinationAccount = A(to),
                DestinationAmount = deliver,
                SendMax = sendMax,
                SearchLevel = PathTestSearchLevel,
            });

    /// <summary>The alternative paying in <paramref name="currency"/>; null when there is none.</summary>
    private static PathFindAlternative In(PathFindResult result, string currency) =>
        result.Alternatives.SingleOrDefault(a => a.SourceAmount.Asset.Currency == currency);

    /// <summary>The paths as account names, each path a list, compared as a set as Path_test's <c>same</c> does.</summary>
    private static void AssertPaths(PathFindAlternative alternative, params string[][] expected)
    {
        Dictionary<string, string> names = Addresses.ToDictionary(e => e.Value, e => e.Key);
        HashSet<string> actual = alternative.PathsComputed
            .Select(path => string.Join(",", path.Select(step => step.Account != null ? names[step.Account] : $"{step.CurrencyCode}/{step.Issuer}")))
            .ToHashSet();
        HashSet<string> wanted = expected.Select(path => string.Join(",", path)).ToHashSet();
        Assert.IsTrue(actual.SetEquals(wanted), $"paths: expected [{string.Join(" | ", wanted)}], found [{string.Join(" | ", actual)}]");
    }

    [TestMethod]
    public void JtxAccountsAreRippledsOwn()
    {
        Assert.AreEqual("rG1QQv2nh2gr7RCZ1P8YYcBUKCCN633jCn", A("alice"));
        Assert.AreEqual("rPMh7Pi9ct699iZUTWaytJUoHcJ7cgyziK", A("bob"));
    }

    [TestMethod]
    public async Task NoDirectPathNoIntermediaryNoAlternatives()
    {
        PathFindResult result = await Find(new Ledger("alice", "bob"), "alice", "bob", Amount("USD", "bob", "5"));
        Assert.IsNull(result.Error);
        Assert.IsNull(In(result, "USD"));
    }

    [TestMethod]
    public async Task DirectPathNoIntermediary()
    {
        Ledger ledger = new Ledger("alice", "bob").Trust("bob", "alice", "700");
        PathFindAlternative usd = In(await Find(ledger, "alice", "bob", Amount("USD", "bob", "5")), "USD");
        Assert.IsNotNull(usd);
        AssertPaths(usd);
        Assert.AreEqual(Amount("USD", "alice", "5"), usd.SourceAmount);
    }

    [TestMethod]
    public async Task PathFind()
    {
        Ledger ledger = new Ledger("alice", "bob", "gateway")
            .Trust("alice", "gateway", "600").Trust("bob", "gateway", "700")
            .Holds("alice", "gateway", "70").Holds("bob", "gateway", "50");
        PathFindAlternative usd = In(await Find(ledger, "alice", "bob", Amount("USD", "bob", "5")), "USD");
        AssertPaths(usd, new[] { "gateway" });
        Assert.AreEqual(Amount("USD", "alice", "5"), usd.SourceAmount);
    }

    [TestMethod]
    public async Task XrpToXrp()
    {
        // The engine does not move XRP straight between accounts (isDirectXrpToXrp): no alternative.
        PathFindResult result = await Find(new Ledger("alice", "bob"), "alice", "bob", Drops(5_000_000));
        Assert.IsNull(result.Error);
        Assert.IsEmpty(result.Alternatives);
    }

    [TestMethod]
    public async Task PathFindConsumeAll()
    {
        Ledger ledger = new Ledger("alice", "bob", "carol", "dan", "edward")
            .Trust("bob", "alice", "10").Trust("carol", "bob", "10").Trust("edward", "carol", "10")
            .Trust("dan", "alice", "100").Trust("edward", "dan", "100");
        PathFindAlternative usd = In(await Find(ledger, "alice", "edward", Amount("USD", "edward", "-1")), "USD");
        AssertPaths(usd, new[] { "dan" }, new[] { "bob", "carol" });
        Assert.AreEqual(Amount("USD", "alice", "110"), usd.SourceAmount);
        Assert.AreEqual(Amount("USD", "edward", "110"), usd.DestinationAmount);
    }

    [TestMethod]
    public async Task PathFindConsumeAllThroughAnOffer()
    {
        Ledger ledger = new Ledger("alice", "bob", "carol", "gateway")
            .Trust("bob", "gateway", "100").Trust("carol", "gateway", "100")
            .Holds("carol", "gateway", "100")
            .Offer("carol", Drops(100_000_000), Amount("USD", "gateway", "100"));

        PathFindResult aud = await Find(ledger, "alice", "bob", Amount("AUD", "bob", "-1"), Drops(1_000_000_000_000));
        Assert.IsEmpty(aud.Alternatives);

        PathFindAlternative usd = In(await Find(ledger, "alice", "bob", Amount("USD", "bob", "-1"), Drops(1_000_000_000_000)), "XRP");
        Assert.AreEqual(Drops(100_000_000), usd.SourceAmount);
        Assert.AreEqual(Amount("USD", "bob", "100"), usd.DestinationAmount);
    }

    [TestMethod]
    public async Task AlternativePathsLimitReturnedPathsToBestQuality()
    {
        Ledger ledger = new Ledger("alice", "bob", "carol", "dan", "gateway", "gateway2")
            .Rate("carol", 1_100_000_000)
            .Trust("alice", "carol", "800").Trust("bob", "carol", "800")
            .Trust("alice", "dan", "800").Trust("bob", "dan", "800")
            .Trust("alice", "gateway", "800").Trust("bob", "gateway", "800")
            .Trust("alice", "gateway2", "800").Trust("bob", "gateway2", "800")
            .Trust("dan", "alice", "800").Trust("dan", "bob", "800")
            .Holds("alice", "gateway2", "100").Holds("alice", "carol", "100").Holds("alice", "gateway", "100");
        PathFindAlternative usd = In(await Find(ledger, "alice", "bob", Amount("USD", "bob", "5")), "USD");
        AssertPaths(usd, new[] { "gateway" }, new[] { "gateway2" }, new[] { "dan" }, new[] { "carol" });
        Assert.AreEqual(Amount("USD", "alice", "5"), usd.SourceAmount);
    }

    [TestMethod]
    public async Task PathNegativeIssue5()
    {
        Ledger ledger = new Ledger("alice", "bob", "carol", "dan")
            .Trust("alice", "bob", "100").Trust("carol", "bob", "100").Trust("dan", "bob", "100")
            .Trust("dan", "alice", "100").Trust("dan", "carol", "100")
            .Holds("carol", "bob", "75");
        Assert.IsNull(In(await Find(ledger, "alice", "bob", Amount("USD", "bob", "25")), "USD"));
    }

    [TestMethod]
    public async Task IndirectPath()
    {
        Ledger ledger = new Ledger("alice", "bob", "carol").Trust("bob", "alice", "1000").Trust("carol", "bob", "1000");
        PathFindAlternative usd = In(await Find(ledger, "alice", "carol", Amount("USD", "carol", "5")), "USD");
        AssertPaths(usd, new[] { "bob" });
        Assert.AreEqual(Amount("USD", "alice", "5"), usd.SourceAmount);
    }

    [TestMethod]
    public async Task MalformedRequestsAreRefused()
    {
        Ledger ledger = new Ledger("alice", "bob");
        Assert.AreEqual("dstAmtMalformed", (await Find(ledger, "alice", "bob", Amount("USD", "bob", "0"))).Error);
        Assert.AreEqual("dstAmtMalformed", (await Find(ledger, "alice", "bob", Amount("USD", "bob", "5"), Drops(1))).Error);
        Assert.AreEqual("srcActNotFound", (await Find(ledger, "carol", "bob", Amount("USD", "bob", "5"))).Error);
        Assert.AreEqual("actNotFound", (await Find(ledger, "alice", "carol", Amount("USD", "carol", "5"))).Error);
    }

    [TestMethod]
    public void BookIndexNormalizesCurrencies()
    {
        BookIndex index = new BookIndex();
        index.Add(new IssuedCurrency { Currency = "0000000000000000000000005553440000000000", Issuer = A("gateway") }, Xrp);
        Assert.IsTrue(index.HasBookToXrp(Iou("USD", "gateway"), null));
        Assert.HasCount(1, index.BooksFrom(Iou("USD", "gateway"), null));
        Assert.IsEmpty(index.BooksFrom(Iou("USD", "gateway"), new string('A', 64)));
    }

    [TestMethod]
    public async Task BookIndexRefusesTooManyAssets()
    {
        // Refused before the node is asked anything, so the client never connects.
        global::Xrpl.Client.XrplClient client = new global::Xrpl.Client.XrplClient("ws://127.0.0.1:1");
        IssuedCurrency[] assets = Enumerable.Range(0, BookIndex.MaxProbeAssets)
            .Select(i => Iou("USD", "holder" + i))
            .ToArray();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => BookIndex.FromAssetsAsync(client, assets));
    }
}
