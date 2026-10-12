using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// MPTs in the payment engine (MPTokensV2): the strands <c>toStrand</c> builds around
/// <c>MPTEndpointStep</c>s, as rippled's <c>PayStrandMPT_test.cpp</c> pins them, and crossings and
/// payments worked out by hand - transfer fees rounded up on the owner, MPTokens created, an
/// issuer's own offer bounded by what it may still issue, locks and trade permissions.
/// </summary>
[TestClass]
public class TestUMptDex
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Carol = "carol";
    private const ulong Fee = 10;

    // The issuer must be a real address: an MPT's issuance ID names it.
    private static readonly string Gw = XrplWallet.Generate().ClassicAddress;
    private static readonly string UsdId = ParseMPTID.GenerateMPTokenIssuanceID(1, Gw);
    private static readonly string EurId = ParseMPTID.GenerateMPTokenIssuanceID(2, Gw);
    private static readonly IssuedCurrency Usd = new IssuedCurrency { MptIssuanceId = UsdId };
    private static readonly IssuedCurrency Eur = new IssuedCurrency { MptIssuanceId = EurId };
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static readonly LedgerRules V2 = new LedgerRules { MPTokensV2 = true };
    private static readonly LedgerRules BeforeV2 = new LedgerRules { MPTokensV2 = false };

    private static XrplAmount Drops(long drops) => XrplAmount.FromNumber(Xrp, (XrplNumber)drops);

    private static XrplAmount Units(IssuedCurrency mpt, long units) => XrplAmount.Parse(mpt, units.ToString(CultureInfo.InvariantCulture));

    private static Currency Wire(XrplAmount amount) => amount.ToCurrency();

    private static DexAccount Account(string address) =>
        new DexAccount { Address = address, Balance = 30_000_000_000, OwnerCount = 2, Sequence = 10 };

    private static DexMptIssuance Issuance(
        string id,
        ulong outstanding,
        ulong? maximum = null,
        ushort transferFee = 0,
        bool canTrade = true,
        bool canTransfer = true,
        bool locked = false,
        bool requireAuth = false) =>
        new DexMptIssuance
        {
            MptIssuanceId = id,
            Issuer = Gw,
            OutstandingAmount = outstanding,
            MaximumAmount = maximum,
            TransferFee = transferFee,
            CanTrade = canTrade,
            CanTransfer = canTransfer,
            Locked = locked,
            RequireAuth = requireAuth,
        };

    private static DexMptHolding Holding(string account, string id, ulong amount, bool locked = false, bool authorized = false) =>
        new DexMptHolding { Account = account, MptIssuanceId = id, Amount = amount, Locked = locked, Authorized = authorized };

    private static DexOffer Offer(string index, string owner, XrplAmount takerPays, XrplAmount takerGets) =>
        new DexOffer { Index = index, Account = owner, TakerPays = takerPays, TakerGets = takerGets, Quality = XrplQuality.FromAmounts(takerPays, takerGets) };

    private static DexSnapshot Snapshot(
        IEnumerable<DexMptIssuance> issuances,
        IEnumerable<DexMptHolding> holdings,
        IEnumerable<DexOffer> offers = null) =>
        new DexSnapshot
        {
            ParentCloseTime = 800_000_000,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = new[] { Account(Alice), Account(Bob), Account(Carol), Account(Gw) },
            MptIssuances = issuances.ToList(),
            MptHoldings = holdings.ToList(),
            Offers = (offers ?? Array.Empty<DexOffer>()).ToList(),
        };

    private static XrplAmount Change(IReadOnlyList<BalanceChange> changes, string account, IssuedCurrency asset) =>
        changes
            .Where(c => c.Account == account && XrplAmount.SameAsset(c.Change.Asset, asset))
            .Select(c => c.Change)
            .DefaultIfEmpty(XrplAmount.Zero(asset))
            .Single();

    // ---- strands ----

    private static PathElement Ipe(IssuedCurrency asset) => PathElement.OfAsset(asset);

    private static PathElement Cpe(string currency) => new PathElement(null, currency, null);

    /// <summary><c>toStrand</c> as a payment builds it: the result and the steps as M(src,dst), B(in,out), X(account).</summary>
    private static (string Result, string Steps) ToStrand(
        DexSnapshot snapshot,
        IssuedCurrency deliver,
        IssuedCurrency sendMax,
        string destination = Bob,
        params PathElement[] path)
    {
        DexView view = new DexView(new DexWorld(snapshot), V2);
        StrandBuilder.Request request = new StrandBuilder.Request
        {
            Source = Alice,
            Destination = destination,
            Deliver = deliver,
            SendMax = sendMax,
            AmmContext = new AmmFlowContext(Alice),
        };
        (string result, List<FlowStep> strand) = StrandBuilder.ToStrand(view, request, path);
        return result != null ? (result, null) : ("tesSUCCESS", string.Join(" ", strand.Select(Describe)));
    }

    private static string Describe(FlowStep step) => step switch
    {
        MptEndpointStep m => $"M({Name(m.DirectStepAccounts.Value.Source)},{Name(m.DirectStepAccounts.Value.Destination)})",
        XrpEndpointStep x => $"X({(x.DirectStepAccounts.Value.Source.Length == 0 ? x.DirectStepAccounts.Value.Destination : x.DirectStepAccounts.Value.Source)})",
        DirectStep d => $"D({d.DirectStepAccounts.Value.Source},{d.DirectStepAccounts.Value.Destination},{d.Currency})",
        _ => $"B({Name(step.BookStepBook.Value.In)},{Name(step.BookStepBook.Value.Out)})",
    };

    private static string Name(string account) => account == Gw ? "gw" : account;

    private static string Name(IssuedCurrency asset) =>
        XrplAmount.KindOf(asset) == AmountKind.Xrp ? "XRP"
        : string.Equals(asset.MptIssuanceId, UsdId, StringComparison.OrdinalIgnoreCase) ? "usd"
        : "eur";

    private static DexMptHolding[] AuthorizedHolders(string id, ulong aliceHolds) =>
        new[] { Holding(Alice, id, aliceHolds), Holding(Bob, id, 0), Holding(Carol, id, 0) };

    [TestMethod]
    public void Strand_HoldersNeedMpTokensAndFunds()
    {
        DexSnapshot none = Snapshot(new[] { Issuance(UsdId, 0) }, Array.Empty<DexMptHolding>());
        Assert.AreEqual("tecNO_AUTH", ToStrand(none, Usd, null).Result);

        DexSnapshot empty = Snapshot(new[] { Issuance(UsdId, 0) }, AuthorizedHolders(UsdId, 0));
        Assert.AreEqual("tecPATH_DRY", ToStrand(empty, Usd, null).Result);
    }

    [TestMethod]
    public void Strand_ImpliedIssuerAndBooks()
    {
        DexSnapshot funded = Snapshot(
            new[] { Issuance(UsdId, 100), Issuance(EurId, 0) },
            AuthorizedHolders(UsdId, 100).Concat(AuthorizedHolders(EurId, 0)));

        Assert.AreEqual(("tesSUCCESS", "M(alice,gw) M(gw,bob)"), ToStrand(funded, Usd, null));
        Assert.AreEqual(("tesSUCCESS", "M(alice,gw) B(usd,eur) M(gw,bob)"), ToStrand(funded, Eur, Usd));
        Assert.AreEqual(("tesSUCCESS", "M(alice,gw) B(usd,eur) M(gw,bob)"), ToStrand(funded, Eur, Usd, Bob, Ipe(Eur)));
        Assert.AreEqual(("tesSUCCESS", "X(alice) B(XRP,usd) M(gw,bob)"), ToStrand(funded, Usd, Xrp, Bob, Ipe(Usd)));
        Assert.AreEqual(("tesSUCCESS", "M(alice,gw) B(usd,XRP) X(bob)"), ToStrand(funded, Xrp, Usd, Bob, Cpe("XRP")));
        Assert.AreEqual(("tesSUCCESS", "M(alice,gw) B(usd,XRP) B(XRP,eur) M(gw,bob)"), ToStrand(funded, Eur, Usd, Bob, Cpe("XRP")));

        // A pure redeem to the issuer is one step.
        Assert.AreEqual(("tesSUCCESS", "M(alice,gw)"), ToStrand(funded, Usd, null, Gw));
    }

    [TestMethod]
    public void Strand_MalformedAndLoopingPaths()
    {
        DexSnapshot funded = Snapshot(
            new[] { Issuance(UsdId, 100), Issuance(EurId, 0) },
            AuthorizedHolders(UsdId, 100).Concat(AuthorizedHolders(EurId, 0)));

        Assert.AreEqual("temBAD_PATH", ToStrand(funded, Eur, Usd, Bob, Ipe(Usd), Ipe(Eur)).Result, "a book from usd to usd");
        Assert.AreEqual("temBAD_PATH_LOOP", ToStrand(funded, Eur, Usd, Bob, Ipe(Eur), Ipe(Usd), Ipe(Eur)).Result);

        // An MPT does not ripple: no account after it, and no currency with it.
        Assert.AreEqual("temBAD_PATH", ToStrand(funded, Eur, Usd, Bob, Ipe(Eur), PathElement.OfAccount(Carol)).Result);
        Assert.AreEqual("temBAD_PATH", ToStrand(funded, Eur, Usd, Bob, new PathElement(null, "USD", null, EurId)).Result);
    }

    [TestMethod]
    public void Strand_LocksAndTradePermission()
    {
        DexMptHolding[] holders(bool aliceLocked = false, bool bobLocked = false) => new[]
        {
            Holding(Alice, UsdId, 100, aliceLocked), Holding(Bob, UsdId, 0, bobLocked),
            Holding(Alice, EurId, 0), Holding(Bob, EurId, 0),
        };

        Assert.AreEqual("terLOCKED", ToStrand(Snapshot(new[] { Issuance(UsdId, 100) }, holders(aliceLocked: true)), Usd, null).Result, "the sender's MPToken");
        Assert.AreEqual("terLOCKED", ToStrand(Snapshot(new[] { Issuance(UsdId, 100, locked: true) }, holders()), Usd, null).Result, "the issuance");
        Assert.AreEqual("terLOCKED", ToStrand(Snapshot(new[] { Issuance(UsdId, 100) }, holders(bobLocked: true)), Usd, null).Result, "the receiver's MPToken");

        // Between holders the MPT must be transferable; through a book, tradable.
        Assert.AreEqual("tecNO_AUTH", ToStrand(Snapshot(new[] { Issuance(UsdId, 100, canTransfer: false) }, holders()), Usd, null).Result);
        Assert.AreEqual(
            "tecNO_PERMISSION",
            ToStrand(Snapshot(new[] { Issuance(UsdId, 100, canTrade: false), Issuance(EurId, 0) }, holders()), Eur, Usd).Result);
    }

    // ---- offer crossing ----

    /// <summary>bob's 100 usd at 10,000 drops each; alice holds 1000 XRP-worth of room and the usd holding given.</summary>
    private static DexSnapshot MakerBook(DexMptIssuance usd, bool aliceHasToken = true, ulong bobHolds = 1000) =>
        Snapshot(
            new[] { usd },
            aliceHasToken ? new[] { Holding(Alice, UsdId, 0), Holding(Bob, UsdId, bobHolds) } : new[] { Holding(Bob, UsdId, bobHolds) },
            new[] { Offer("B1", Bob, Drops(1_000_000), Units(Usd, 100)) });

    [TestMethod]
    public void Cross_MptForXrp_AtTheRestingOffersPrice()
    {
        OfferCrossingResult result = OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000)), Alice, Units(Usd, 40), Drops(400_000), Fee, rules: V2);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Units(Usd, 40), Change(result.BalanceChanges, Alice, Usd));
        Assert.AreEqual(Drops(-400_010), Change(result.BalanceChanges, Alice, Xrp), "400,000 drops and the fee");
        Assert.AreEqual(Units(Usd, -40), Change(result.BalanceChanges, Bob, Usd));
        Assert.AreEqual(Drops(400_000), Change(result.BalanceChanges, Bob, Xrp));
        Assert.AreEqual(XrplAmount.Zero(Usd), Change(result.BalanceChanges, Gw, Usd), "redeemed and issued again: OutstandingAmount unchanged");
        Assert.HasCount(1, result.Offers);
        Assert.AreEqual(Drops(600_000), result.Offers[0].TakerPays);
        Assert.AreEqual(Units(Usd, 60), result.Offers[0].TakerGets);
        Assert.IsEmpty(result.MpTokens);
        Assert.IsNull(result.PlacedTakerPays);
    }

    /// <summary>
    /// An MPT side needs MPTokensV2, in the amounts overload as in the transaction one: before it,
    /// the node refuses the offer in preflight (temDISABLED) and nothing is crossed or charged.
    /// </summary>
    [TestMethod]
    public void Cross_MptBeforeMPTokensV2_IsDisabled()
    {
        OfferCrossingResult result = OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000)), Alice, Units(Usd, 40), Drops(400_000), Fee, rules: BeforeV2);

        Assert.AreEqual("temDISABLED", result.EngineResult);
        Assert.IsEmpty(result.BalanceChanges);
    }

    [TestMethod]
    public void Cross_CreatesTheTakersMpToken()
    {
        OfferCrossingResult result = OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000), aliceHasToken: false), Alice, Units(Usd, 40), Drops(400_000), Fee, rules: V2);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.HasCount(1, result.MpTokens);
        Assert.AreEqual(Alice, result.MpTokens[0].Account);
        Assert.AreEqual(UsdId, result.MpTokens[0].MptIssuanceId);
        Assert.AreEqual(Units(Usd, 40), Change(result.BalanceChanges, Alice, Usd));

        // An issuance that needs authorization refuses a taker without an MPToken.
        OfferCrossingResult refused = OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000, requireAuth: true), aliceHasToken: false), Alice, Units(Usd, 40), Drops(400_000), Fee, rules: V2);
        Assert.AreEqual("tecNO_AUTH", refused.EngineResult);
    }

    /// <summary>
    /// A 1% transfer fee: crossing, the offer's owner pays it on what it gives, rounded up -
    /// 10.1 units become 11, and the issuer keeps the unit over.
    /// </summary>
    [TestMethod]
    public void Cross_TransferFeeRoundsUpOnTheOwner()
    {
        OfferCrossingResult result = OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000, transferFee: 1000)), Alice, Units(Usd, 10), Drops(100_000), Fee, rules: V2);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Units(Usd, 10), Change(result.BalanceChanges, Alice, Usd));
        Assert.AreEqual(Units(Usd, -11), Change(result.BalanceChanges, Bob, Usd));
        Assert.AreEqual(Units(Usd, 1), Change(result.BalanceChanges, Gw, Usd), "OutstandingAmount down by the fee");
        Assert.AreEqual(Drops(100_000), Change(result.BalanceChanges, Bob, Xrp));
        Assert.AreEqual(Units(Usd, 90), result.Offers.Single().TakerGets);
    }

    /// <summary>
    /// The issuer's own offer gives no more than it may still issue: 50 of its 100 units, with
    /// OutstandingAmount at 950 of 1000. The offer is then unfunded and removed, and the rest of
    /// alice's offer is placed.
    /// </summary>
    [TestMethod]
    public void Cross_IssuersOwnOfferStopsAtTheMaximum()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Issuance(UsdId, 950, maximum: 1000) },
            new[] { Holding(Alice, UsdId, 0), Holding(Bob, UsdId, 950) },
            new[] { Offer("G1", Gw, Drops(1_000_000), Units(Usd, 100)) });

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Alice, Units(Usd, 80), Drops(800_000), Fee, rules: V2);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Units(Usd, 50), Change(result.BalanceChanges, Alice, Usd));
        Assert.AreEqual(Drops(-500_010), Change(result.BalanceChanges, Alice, Xrp));
        Assert.AreEqual(Units(Usd, -50), Change(result.BalanceChanges, Gw, Usd), "issued up to the maximum");
        Assert.AreEqual(Drops(500_000), Change(result.BalanceChanges, Gw, Xrp));

        OfferChange issuers = result.Offers.Single();
        Assert.IsTrue(issuers.Deleted);
        Assert.AreEqual(OfferRemovalReason.Unfunded, issuers.Reason);
        Assert.AreEqual(Units(Usd, 50), issuers.FilledTakerGets);

        Assert.AreEqual(Units(Usd, 30), result.PlacedTakerPays);
        Assert.AreEqual(Drops(300_000), result.PlacedTakerGets);
    }

    [TestMethod]
    public void Cross_PreclaimRefusals()
    {
        Assert.AreEqual(
            "tecLOCKED",
            OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000, locked: true)), Alice, Units(Usd, 10), Drops(100_000), Fee, rules: V2).EngineResult);
        Assert.AreEqual(
            "tecNO_PERMISSION",
            OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000, canTrade: false)), Alice, Units(Usd, 10), Drops(100_000), Fee, rules: V2).EngineResult);

        OfferCreate offer = new OfferCreate
        {
            Account = Alice,
            TakerPays = Wire(Units(Usd, 10)),
            TakerGets = Wire(Drops(100_000)),
            Fee = new Currency { Value = "10" },
        };
        OfferCrossingResult disabled = OfferCreateCrossing.Cross(MakerBook(Issuance(UsdId, 1000)), offer, BeforeV2);
        Assert.AreEqual("temDISABLED", disabled.EngineResult);
        Assert.IsFalse(disabled.Applied);
    }

    // ---- payments ----

    private static Payment Pay(string from, string to, XrplAmount amount, XrplAmount? sendMax = null, PaymentFlags flags = 0) =>
        new Payment
        {
            Account = from,
            Destination = to,
            Amount = Wire(amount),
            SendMax = sendMax is { } max ? Wire(max) : null,
            Flags = flags,
            Fee = new Currency { Value = "10" },
        };

    private static DexSnapshot Holders(ushort transferFee, bool locked = false) =>
        Snapshot(
            new[] { Issuance(UsdId, 1000, transferFee: transferFee) },
            new[] { Holding(Alice, UsdId, 1000, locked), Holding(Bob, UsdId, 0) });

    /// <summary>Between holders the 1% fee is issued on top: 100 delivered costs 101, which SendMax must allow.</summary>
    [TestMethod]
    public void Pay_BetweenHolders_ChargesTheTransferFee()
    {
        foreach (LedgerRules rules in new[] { V2, BeforeV2 })
        {
            PaymentFlowResult paid = PaymentFlow.Evaluate(Holders(1000), Pay(Alice, Bob, Units(Usd, 100), Units(Usd, 101)), rules);
            Assert.AreEqual("tesSUCCESS", paid.EngineResult, rules.MPTokensV2 ? "MPTokensV2" : "before it");
            Assert.AreEqual(Units(Usd, 100), paid.DeliveredAmount);
            Assert.AreEqual(Units(Usd, 101), paid.Paid);
            Assert.AreEqual(Units(Usd, -101), Change(paid.BalanceChanges, Alice, Usd));
            Assert.AreEqual(Units(Usd, 100), Change(paid.BalanceChanges, Bob, Usd));
            Assert.AreEqual(Units(Usd, 1), Change(paid.BalanceChanges, Gw, Usd));

            PaymentFlowResult short_ = PaymentFlow.Evaluate(Holders(1000), Pay(Alice, Bob, Units(Usd, 100)), rules);
            Assert.AreEqual("tecPATH_PARTIAL", short_.EngineResult, "SendMax is the amount when absent");
        }
    }

    /// <summary>
    /// An issuance authorizing through a permissioned domain: a holder whose MPToken the issuer
    /// never authorized holds it by an accepted credential the domain takes, and not without one.
    /// </summary>
    [TestMethod]
    public void Pay_DomainAuthorizesTheHolder()
    {
        string alice = XrplWallet.Generate().ClassicAddress, bob = XrplWallet.Generate().ClassicAddress, carol = XrplWallet.Generate().ClassicAddress;
        const string domainId = "AAAA000000000000000000000000000000000000000000000000000000000001";
        DexSnapshot Snapshot(bool bobHasCredential)
        {
            DexMptIssuance issuance = Issuance(UsdId, 1000, requireAuth: true);
            List<DexCredential> credentials = new List<DexCredential>
            {
                new DexCredential { Subject = alice, Issuer = carol, CredentialType = "4B5943", Accepted = true },
            };
            if (bobHasCredential)
                credentials.Add(new DexCredential { Subject = bob, Issuer = carol, CredentialType = "4B5943", Accepted = true });

            return new DexSnapshot
            {
                ReserveBase = 10_000_000,
                ReserveIncrement = 2_000_000,
                Accounts = new[] { Account(alice), Account(bob), Account(carol), Account(Gw) },
                MptIssuances = new[]
                {
                    new DexMptIssuance
                    {
                        MptIssuanceId = issuance.MptIssuanceId, Issuer = Gw, OutstandingAmount = 1000,
                        CanTrade = true, CanTransfer = true, RequireAuth = true, DomainId = domainId,
                    },
                },
                MptHoldings = new[] { Holding(alice, UsdId, 1000), Holding(bob, UsdId, 0) },
                Domains = new[]
                {
                    new DexDomain { DomainId = domainId, Owner = Gw, AcceptedCredentials = new[] { new DexCredentialType { Issuer = carol, CredentialType = "4B5943" } } },
                },
                Credentials = credentials,
            };
        }

        Assert.AreEqual("tesSUCCESS", PaymentFlow.Evaluate(Snapshot(true), Pay(alice, bob, Units(Usd, 10)), V2).EngineResult);
        Assert.AreEqual("tecNO_AUTH", PaymentFlow.Evaluate(Snapshot(false), Pay(alice, bob, Units(Usd, 10)), V2).EngineResult);
    }

    [TestMethod]
    public void Pay_LockedHolder()
    {
        Assert.AreEqual("tecLOCKED", PaymentFlow.Evaluate(Holders(0, locked: true), Pay(Alice, Bob, Units(Usd, 10)), BeforeV2).EngineResult);

        // Through the engine, the strand's lock is a retry code, charged as tecPATH_DRY.
        Assert.AreEqual("tecPATH_DRY", PaymentFlow.Evaluate(Holders(0, locked: true), Pay(Alice, Bob, Units(Usd, 10)), V2).EngineResult);
    }

    [TestMethod]
    public void Pay_BeforeV2_RefusesPathsAndFlags()
    {
        Payment withPaths = Pay(Alice, Bob, Units(Usd, 10));
        withPaths.Paths = new List<List<PathStep>> { new List<PathStep> { new PathStep { CurrencyCode = "XRP" } } };
        Assert.AreEqual("temMALFORMED", PaymentFlow.Evaluate(Holders(0), withPaths, BeforeV2).EngineResult);
        Assert.AreEqual("temINVALID_FLAG", PaymentFlow.Evaluate(Holders(0), Pay(Alice, Bob, Units(Usd, 10), flags: PaymentFlags.tfLimitQuality), BeforeV2).EngineResult);
        Assert.AreEqual("temMALFORMED", PaymentFlow.Evaluate(Holders(0), Pay(Alice, Bob, Units(Usd, 10), Drops(100)), BeforeV2).EngineResult);
    }

    /// <summary>
    /// XRP to usd through bob's offer with a 1% fee: the issuer issues 30 to carol and takes 31
    /// from the book - 30.3 rounded up - which costs 310,000 drops.
    /// </summary>
    [TestMethod]
    public void Pay_XrpToMpt_ThroughTheBook()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Issuance(UsdId, 1000, transferFee: 1000) },
            new[] { Holding(Bob, UsdId, 1000), Holding(Carol, UsdId, 0) },
            new[] { Offer("B1", Bob, Drops(1_000_000), Units(Usd, 100)) });

        PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, Pay(Alice, Carol, Units(Usd, 30), Drops(400_000)), V2);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Units(Usd, 30), result.DeliveredAmount);
        Assert.AreEqual(Drops(310_000), result.Paid);
        Assert.AreEqual(Units(Usd, -31), Change(result.BalanceChanges, Bob, Usd));
        Assert.AreEqual(Units(Usd, 30), Change(result.BalanceChanges, Carol, Usd));
        Assert.AreEqual(Units(Usd, 1), Change(result.BalanceChanges, Gw, Usd));
        Assert.AreEqual(Drops(-310_010), Change(result.BalanceChanges, Alice, Xrp));
        Assert.AreEqual(Units(Usd, 69), result.Offers.Single().TakerGets);
        Assert.AreEqual(Drops(690_000), result.Offers.Single().TakerPays);

        // A book of an MPT that may not trade is refused when the strand is built.
        DexSnapshot untradable = Snapshot(
            new[] { Issuance(UsdId, 1000, canTrade: false) },
            new[] { Holding(Bob, UsdId, 1000), Holding(Carol, UsdId, 0) },
            new[] { Offer("B1", Bob, Drops(1_000_000), Units(Usd, 100)) });
        Assert.AreEqual("tecNO_PERMISSION", PaymentFlow.Evaluate(untradable, Pay(Alice, Carol, Units(Usd, 30), Drops(400_000)), V2).EngineResult);
    }

    /// <summary>getBookBase's MPT branches: a one-byte tag, the issuance ID in place of a currency and issuer.</summary>
    [TestMethod]
    public void BookBase_TagsMptBooks()
    {
        string open = DexSnapshot.BookBase(Xrp, Usd, null);
        string reverse = DexSnapshot.BookBase(Usd, Xrp, null);
        string both = DexSnapshot.BookBase(Usd, Eur, null);
        Assert.AreNotEqual(open, reverse);
        Assert.AreNotEqual(open, both);
        StringAssert.EndsWith(open, "0000000000000000");
        Assert.AreEqual(64, both.Length);
    }
}
