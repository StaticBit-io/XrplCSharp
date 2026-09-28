using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// The strands built from a payment's path, against rippled's <c>PayStrand_test.cpp</c>
/// (<c>testToStrand</c>): the implied accounts and books, XRP ends and bridges, the malformed and
/// looping paths, and the lines that refuse - missing, NoRipple, frozen, unauthorized.
/// </summary>
[TestClass]
public class TestUStrandBuilder
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Carol = "carol";
    private const string Gw = "gw";

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = Gw };
    private static readonly IssuedCurrency Eur = new IssuedCurrency { Currency = "EUR", Issuer = Gw };
    private static readonly IssuedCurrency CarolUsd = new IssuedCurrency { Currency = "USD", Issuer = Carol };

    private static DexAccount Account(string address, bool globalFreeze = false, bool requireAuth = false) =>
        new DexAccount { Address = address, Balance = 10_000_000_000, GlobalFreeze = globalFreeze, RequireAuth = requireAuth };

    private static DexTrustLine Line(string holder, IssuedCurrency asset, string balance = "0", bool peerNoRipple = false, bool frozen = false, bool peerAuthorized = false) =>
        new DexTrustLine
        {
            Account = holder,
            Balance = XrplAmount.Parse(asset, balance),
            Limit = XrplAmount.Parse(asset, "1000"),
            PeerNoRipple = peerNoRipple,
            Frozen = frozen,
            PeerAuthorized = peerAuthorized,
        };

    private static DexSnapshot Snapshot(IEnumerable<DexAccount> accounts, params DexTrustLine[] lines) =>
        new DexSnapshot { ReserveBase = 10_000_000, ReserveIncrement = 2_000_000, Accounts = accounts.ToList(), TrustLines = lines };

    /// <summary>alice, bob, carol and gw, with USD and EUR lines to gw; alice and carol hold 100 USD.</summary>
    private static DexSnapshot Funded(params DexTrustLine[] extra) => Snapshot(
        new[] { Account(Alice), Account(Bob), Account(Carol), Account(Gw) },
        new[]
        {
            Line(Alice, Usd, "100"), Line(Bob, Usd), Line(Carol, Usd, "100"),
            Line(Alice, Eur), Line(Bob, Eur),
        }.Concat(extra).ToArray());

    private static PathElement Ipe(IssuedCurrency asset) => new PathElement(null, asset.Currency, asset.Issuer);

    private static PathElement Cpe(string currency) => new PathElement(null, currency, null);

    private static PathElement Ape(string account) => PathElement.OfAccount(account);

    private static PathElement Iape(string issuer) => new PathElement(null, null, issuer);

    /// <summary><c>toStrand</c> from alice to bob, the result code and the steps written as D(src,dst,cur), B(in,out), X(account).</summary>
    private static (string Result, string Steps) ToStrand(DexSnapshot snapshot, IssuedCurrency deliver, IssuedCurrency sendMax, params PathElement[] path)
    {
        DexView view = new DexView(new DexWorld(snapshot), new LedgerRules());
        StrandBuilder.Request request = new StrandBuilder.Request
        {
            Source = Alice,
            Destination = Bob,
            Deliver = deliver,
            SendMax = sendMax,
            AmmContext = new AmmFlowContext(Alice),
        };
        (string result, List<FlowStep> strand) = StrandBuilder.ToStrand(view, request, path);
        if (result != null)
            return (result, null);

        return ("tesSUCCESS", string.Join(" ", strand.Select(Describe)));
    }

    private static string Describe(FlowStep step) => step switch
    {
        DirectStep d => $"D({d.DirectStepAccounts.Value.Source},{d.DirectStepAccounts.Value.Destination},{d.Currency})",
        XrpEndpointStep x => $"X({(x.DirectStepAccounts.Value.Source.Length == 0 ? x.DirectStepAccounts.Value.Destination : x.DirectStepAccounts.Value.Source)})",
        _ => $"B({Name(step.BookStepBook.Value.In)},{Name(step.BookStepBook.Value.Out)})",
    };

    private static string Name(IssuedCurrency asset) => XrplAmount.KindOf(asset) == AmountKind.Xrp ? "XRP" : $"{asset.Currency}.{asset.Issuer}";

    [TestMethod]
    public void MissingAndEmptyLines()
    {
        DexSnapshot bare = Snapshot(new[] { Account(Alice), Account(Bob), Account(Carol), Account(Gw) });
        Assert.AreEqual("terNO_LINE", ToStrand(bare, Usd, null).Result);

        DexSnapshot empty = Snapshot(
            new[] { Account(Alice), Account(Bob), Account(Carol), Account(Gw) },
            Line(Alice, Usd), Line(Bob, Usd), Line(Carol, Usd));
        Assert.AreEqual("tecPATH_DRY", ToStrand(empty, Usd, null).Result);
    }

    [TestMethod]
    public void ImpliedAccountsAndOffers()
    {
        DexSnapshot funded = Funded(Line(Bob, CarolUsd));
        Assert.AreEqual(("tesSUCCESS", "D(alice,gw,USD) D(gw,bob,USD)"), ToStrand(funded, Usd, null));
        Assert.AreEqual(("tesSUCCESS", "D(alice,gw,USD) B(USD.gw,EUR.gw) D(gw,bob,EUR)"), ToStrand(funded, Eur, Usd));
        Assert.AreEqual(("tesSUCCESS", "D(alice,gw,USD) B(USD.gw,EUR.gw) D(gw,bob,EUR)"), ToStrand(funded, Eur, Usd, Ipe(Eur)));
        Assert.AreEqual(("tesSUCCESS", "D(alice,gw,USD) B(USD.gw,USD.carol) D(carol,bob,USD)"), ToStrand(funded, CarolUsd, Usd, Iape(Carol)));
    }

    [TestMethod]
    public void XrpEndsAndTheXrpBridge()
    {
        DexSnapshot funded = Funded();
        Assert.AreEqual(("tesSUCCESS", "X(alice) B(XRP,USD.gw) D(gw,bob,USD)"), ToStrand(funded, Usd, Xrp, Ipe(Usd)));
        Assert.AreEqual(("tesSUCCESS", "D(alice,gw,USD) B(USD.gw,XRP) X(bob)"), ToStrand(funded, Xrp, Usd, Cpe("XRP")));
        Assert.AreEqual(("tesSUCCESS", "D(alice,gw,USD) B(USD.gw,XRP) B(XRP,EUR.gw) D(gw,bob,EUR)"), ToStrand(funded, Eur, Usd, Cpe("XRP")));
    }

    [TestMethod]
    public void MalformedAndLoopingPaths()
    {
        DexSnapshot funded = Funded();
        Assert.AreEqual("temBAD_PATH", ToStrand(funded, Xrp, null, Ape(Carol)).Result);
        Assert.AreEqual("temBAD_PATH", ToStrand(funded, Eur, Usd, Ipe(Usd), Ipe(Eur)).Result);
        Assert.AreEqual("temBAD_PATH", ToStrand(funded, Usd, null, new PathElement(null, null, null)).Result);
        Assert.AreEqual("temBAD_PATH_LOOP", ToStrand(funded, Usd, null, Ape(Gw), Ape(Carol)).Result);
        Assert.AreEqual("temBAD_PATH_LOOP", ToStrand(funded, Eur, Usd, Ipe(Eur), Ipe(Usd), Ipe(Eur)).Result);
    }

    [TestMethod]
    public void NoRippleThroughTheIssuer()
    {
        // gw without DefaultRipple: its side of both lines has NoRipple.
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Alice), Account(Bob), Account(Gw) },
            Line(Alice, Usd, "100", peerNoRipple: true), Line(Bob, Usd, peerNoRipple: true));
        Assert.AreEqual("terNO_RIPPLE", ToStrand(snapshot, Usd, null).Result);
    }

    [TestMethod]
    public void Freezes()
    {
        DexTrustLine[] lines = { Line(Alice, Usd, "100"), Line(Bob, Usd) };
        Assert.AreEqual("tesSUCCESS", ToStrand(Snapshot(new[] { Account(Alice, globalFreeze: true), Account(Bob), Account(Gw) }, lines), Usd, null).Result);
        Assert.AreEqual("terNO_LINE", ToStrand(Snapshot(new[] { Account(Alice), Account(Bob), Account(Gw, globalFreeze: true) }, lines), Usd, null).Result);
        Assert.AreEqual("terNO_LINE", ToStrand(Snapshot(new[] { Account(Alice), Account(Bob, globalFreeze: true), Account(Gw) }, lines), Usd, null).Result);

        DexSnapshot frozenLine = Snapshot(new[] { Account(Alice), Account(Bob), Account(Gw) }, Line(Alice, Usd, "100", frozen: true), Line(Bob, Usd));
        Assert.AreEqual("terNO_LINE", ToStrand(frozenLine, Usd, null).Result);
    }

    [TestMethod]
    public void AuthorizationRequired()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Alice), Account(Bob), Account(Gw, requireAuth: true) },
            Line(Alice, Usd, "100", peerAuthorized: true), Line(Bob, Usd));
        Assert.AreEqual("terNO_AUTH", ToStrand(snapshot, Usd, null).Result);
    }
}
