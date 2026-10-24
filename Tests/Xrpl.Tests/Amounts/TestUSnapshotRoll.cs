using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Utils.Hashes;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.Amounts.SyntheticDexSnapshot;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// <c>SnapshotAfter</c> (issue 301): the snapshot a transaction leaves, with its index derived
/// from the evaluated snapshot's. Along chains of payments and offers - through books and a
/// pool, placing and crossing a resting offer, creating an account, creating and deleting a
/// trust line, moving MPTs - each derived index must be the one indexing afresh gives, the next
/// result through it the one a fresh index gives, and the snapshot must agree with the result's
/// own balance, offer and trust line changes.
/// </summary>
[TestClass]
public class TestUSnapshotRoll
{
    private static readonly string Carol = Address("carol");
    private static readonly LedgerRules V2 = new LedgerRules { MPTokensV2 = true };

    private static IssuedCurrency Eur(string issuer) => new IssuedCurrency { Currency = "EUR", Issuer = issuer };

    private static Payment Pay(string from, string to, XrplAmount amount, XrplAmount? sendMax = null) =>
        new Payment
        {
            Account = from,
            Destination = to,
            Amount = amount.ToCurrency(),
            SendMax = sendMax?.ToCurrency(),
            Fee = new Currency { Value = "10" },
        };

    private static object Cross(DexSnapshot snapshot, string account, XrplAmount takerPays, XrplAmount takerGets, OfferCreateFlags flags = 0) =>
        OfferCreateCrossing.Cross(snapshot, account, takerPays, takerGets, 10, flags);

    private static XrplAmount UsdHeld(DexSnapshot snapshot, string holder, string issuer) => BalanceOf(snapshot, holder, Usd(issuer));

    /// <summary>
    /// Books both ways and the pool, an offer placed then crossed in part and in full, an account
    /// created, a trust line created by crossing and deleted by selling all of it, and refusals.
    /// </summary>
    private static IReadOnlyList<(string Name, Func<DexSnapshot, object> Evaluate)> BookChain { get; } =
        new (string, Func<DexSnapshot, object>)[]
        {
            ("XRP for USD through book and pool", s => PaymentFlow.Evaluate(s, Pay(Alice, Bob, Iou(Gateway(0), "250"), Drops(1_000_000_000_000)))),
            ("offer crossing", s => Cross(s, Alice, Iou(Gateway(1), "250"), Drops(100_000_000_000))),
            // 0.97 XRP for a USD ties the twenty-first offer of its book: it rests after it.
            ("offer placed mid-book, tied", s => Cross(s, Alice, Iou(Gateway(1), "100"), Drops(97_000_000))),
            ("book sold through it", s => Cross(s, Gateway(1), Drops(1), Iou(Gateway(1), "2500"), OfferCreateFlags.tfSell)),
            ("offer placed", s => Cross(s, Alice, XrplAmount.Parse(Eur(Gateway(2)), "10"), Drops(200_000_000))),
            ("placed offer crossed in part", s => Cross(s, Gateway(2), Drops(100_000_000), XrplAmount.Parse(Eur(Gateway(2)), "5"))),
            ("placed offer crossed in full", s => Cross(s, Gateway(2), Drops(100_000_000), XrplAmount.Parse(Eur(Gateway(2)), "5"))),
            ("XRP creating carol", s => PaymentFlow.Evaluate(s, Pay(Alice, Carol, Drops(50_000_000)))),
            ("USD to carol, who has no line", s => PaymentFlow.Evaluate(s, Pay(Alice, Carol, Iou(Gateway(0), "10"), Drops(1_000_000_000)))),
            ("line created by crossing", s => Cross(s, Dave, Iou(Gateway(0), "100"), Drops(100_000_000_000))),
            ("line deleted by selling it all", s => Cross(s, Dave, Drops(1), UsdHeld(s, Dave, Gateway(0)), OfferCreateFlags.tfSell)),
            ("unfunded offer", s => Cross(s, Bob, Drops(100_000_000), Iou(Gateway(2), "50"))),
            ("payment through what the chain left", s => PaymentFlow.Evaluate(s, Pay(Alice, Bob, Iou(Gateway(0), "300"), Drops(1_000_000_000_000)))),
        };

    [TestMethod]
    public void ABookChainRollsAsIndexingAfreshWould()
    {
        List<object> results = RunChain(Build(gateways: 3, owners: 50, offersPerBook: 40, withPool: true), BookChain);

        // What each step is there for, so the chain cannot drift into testing less.
        Assert.IsTrue(Changes(results[0]).Pools.Count > 0, "the payment moves the pool");

        // The tied offer rests after the one it ties, ahead of the worse ones, and the sale takes it there.
        OfferCrossingResult tied = (OfferCrossingResult)results[2];
        Assert.IsNotNull(tied.PlacedTakerPays, "the tied offer rests");
        DexOffer[] book = DexIndex.Of(tied.SnapshotAfter).Book(BookKey.Of(Usd(Gateway(1)), Xrp)).ToArray();
        int at = Array.FindIndex(book, o => o.Account == Alice);
        Assert.IsTrue(at > 0 && at < book.Length - 1, "mid-book");
        Assert.AreEqual(book[at - 1].Quality.Value, book[at].Quality.Value, "after the offer it ties");
        Assert.IsTrue(book[at + 1].Quality.Value > book[at].Quality.Value, "ahead of the worse ones");
        Assert.IsTrue(Changes(results[3]).Offers.Any(o => o.Account == Alice && o.Deleted), "the sale takes the tied offer");

        Assert.IsNotNull(((OfferCrossingResult)results[4]).PlacedTakerPays, "the offer rests");
        Assert.IsTrue(Changes(results[5]).Offers.Any(o => !o.Deleted), "the placed offer is crossed in part");
        Assert.IsTrue(Changes(results[5]).Lines.Any(l => l.Created), "crossing it creates the taker's line");
        Assert.IsTrue(Changes(results[6]).Offers.Any(o => o.Deleted), "the placed offer is crossed in full");
        Assert.AreEqual("tecPATH_DRY", ((PaymentFlowResult)results[8]).EngineResult);
        Assert.IsTrue(Changes(results[9]).Lines.Any(l => l.Created), "the crossing creates a line");
        Assert.IsTrue(Changes(results[10]).Lines.Any(l => l.Deleted), "selling it all deletes the line");
        Assert.AreEqual("tecUNFUNDED_OFFER", ((OfferCrossingResult)results[11]).EngineResult);
    }

    [TestMethod]
    public void AnMptChainRollsAsIndexingAfreshWould()
    {
        string issuer = Address("mpt issuer");
        string id = ParseMPTID.GenerateMPTokenIssuanceID(1, issuer);
        IssuedCurrency mpt = new IssuedCurrency { MptIssuanceId = id };
        XrplAmount Units(long units) => XrplAmount.Parse(mpt, units.ToString(CultureInfo.InvariantCulture));

        DexSnapshot snapshot = new DexSnapshot
        {
            ParentCloseTime = 800_000_000,
            LedgerSequence = 90_000_000,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = new[] { Alice, Bob, Carol, issuer }
                .Select(a => new DexAccount { Address = a, Balance = 30_000_000_000, OwnerCount = 2, Sequence = 10 })
                .ToList(),
            MptIssuances = new[] { new DexMptIssuance { MptIssuanceId = id, Issuer = issuer, OutstandingAmount = 1_000, CanTrade = true, CanTransfer = true } },
            MptHoldings = new[]
            {
                new DexMptHolding { Account = Alice, MptIssuanceId = id, Amount = 600 },
                new DexMptHolding { Account = Bob, MptIssuanceId = id, Amount = 400 },
            },
            Offers = new[]
            {
                new DexOffer
                {
                    Index = new string('B', 64),
                    Account = Bob,
                    TakerPays = Drops(50_000_000),
                    TakerGets = Units(100),
                    Quality = XrplQuality.FromAmounts(Drops(50_000_000), Units(100)),
                },
            },
        };

        List<object> results = RunChain(
            snapshot,
            new (string, Func<DexSnapshot, object>)[]
            {
                ("MPT between holders", s => PaymentFlow.Evaluate(s, Pay(Alice, Bob, Units(100)), V2)),
                ("MPT bought, MPToken created", s => OfferCreateCrossing.Cross(s, Carol, Units(40), Drops(20_000_000), 10, 0, V2)),
                ("MPT issued", s => PaymentFlow.Evaluate(s, Pay(issuer, Carol, Units(10)), V2)),
                ("MPT redeemed", s => PaymentFlow.Evaluate(s, Pay(Carol, issuer, Units(5)), V2)),
            });

        Assert.AreEqual(1, ((OfferCrossingResult)results[1]).MpTokens.Count, "the crossing creates the taker's MPToken");
        Assert.IsTrue(results.All(IsApplied));
    }

    [TestMethod]
    public void AResultNotAppliedLeavesTheSnapshot()
    {
        DexSnapshot snapshot = Build(gateways: 1, owners: 10, offersPerBook: 5);
        OfferCrossingResult result = OfferCreateCrossing.Cross(
            snapshot, Alice, Iou(Gateway(0), "1"), Drops(1_000_000), 10, OfferCreateFlags.tfImmediateOrCancel | OfferCreateFlags.tfFillOrKill);

        Assert.IsFalse(result.Applied);
        Assert.AreSame(snapshot, result.SnapshotAfter);
    }

    [TestMethod]
    public void TheSenderSequenceMovesOnAndAPlacedOfferTakesIt()
    {
        DexSnapshot snapshot = Build(gateways: 3, owners: 10, offersPerBook: 5);
        DexAccount before = AccountOf(snapshot, Alice);

        DexSnapshot after = OfferCreateCrossing.Cross(snapshot, Alice, XrplAmount.Parse(Eur(Gateway(2)), "10"), Drops(200_000_000), 10).SnapshotAfter;

        DexAccount alice = AccountOf(after, Alice);
        Assert.AreEqual(before.Sequence + 1, alice.Sequence);
        Assert.AreEqual(before.OwnerCount + 1, alice.OwnerCount, "the offer placed");
        Assert.AreEqual(before.Balance - 10, alice.Balance, "the fee");

        DexOffer placed = after.Offers.Single(o => o.Index == Hashes.HashOfferId(Alice, before.Sequence));
        Assert.AreEqual(XrplAmount.Parse(Eur(Gateway(2)), "10"), placed.TakerPays);
        Assert.AreEqual(Drops(200_000_000), placed.TakerGets);
    }

    [TestMethod]
    public void ATicketIsConsumedInsteadOfTheSequence()
    {
        DexSnapshot snapshot = Build(gateways: 3, owners: 10, offersPerBook: 5);
        DexAccount before = AccountOf(snapshot, Alice);
        OfferCreate offer = new OfferCreate
        {
            Account = Alice,
            TakerPays = XrplAmount.Parse(Eur(Gateway(2)), "10").ToCurrency(),
            TakerGets = Drops(200_000_000).ToCurrency(),
            Fee = new Currency { Value = "10" },
            Sequence = 0,
            TicketSequence = 77,
        };

        DexSnapshot after = OfferCreateCrossing.Cross(snapshot, offer).SnapshotAfter;

        DexAccount alice = AccountOf(after, Alice);
        Assert.AreEqual(before.Sequence, alice.Sequence);
        Assert.AreEqual(before.OwnerCount, alice.OwnerCount, "one ticket gone, one offer placed");
        Assert.IsTrue(after.Offers.Any(o => o.Index == Hashes.HashOfferId(Alice, 77u)));
    }

    [TestMethod]
    public void AnOfferPlacedWithoutASequenceCannotBeIndexed()
    {
        DexSnapshot built = Build(gateways: 3, owners: 10, offersPerBook: 5);
        DexSnapshot snapshot = With(built, accounts: built.Accounts.Select(a => a.Address == Alice
            ? new DexAccount { Address = Alice, Balance = a.Balance, OwnerCount = a.OwnerCount }
            : a).ToList());

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Alice, XrplAmount.Parse(Eur(Gateway(2)), "10"), Drops(200_000_000), 10);

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.ThrowsExactly<InvalidOperationException>(() => result.SnapshotAfter);
    }

    [TestMethod]
    public void ACreatedAccountTakesTheLedgerSequence()
    {
        DexSnapshot snapshot = Build(gateways: 1, owners: 10, offersPerBook: 5);

        DexSnapshot after = PaymentFlow.Evaluate(snapshot, Pay(Alice, Carol, Drops(50_000_000))).SnapshotAfter;

        DexAccount carol = AccountOf(after, Carol);
        Assert.AreEqual(50_000_000UL, carol.Balance);
        Assert.AreEqual(snapshot.LedgerSequence + 1, carol.Sequence, "the earliest ledger the payment lands in");
        Assert.AreEqual(0u, carol.OwnerCount);
        Assert.IsNull(snapshot.Accounts.FirstOrDefault(a => a.Address == Carol));
    }

    [TestMethod]
    public void AnOfferReplacingTheOnlyOfferOfItsBook()
    {
        string gw = Gateway(0);
        XrplAmount pays = Iou(gw, "10");
        DexSnapshot snapshot = new DexSnapshot
        {
            ParentCloseTime = 800_000_000,
            LedgerSequence = 100,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = new[]
            {
                new DexAccount { Address = gw, Balance = 100_000_000_000, Sequence = 1 },
                new DexAccount { Address = Alice, Balance = 100_000_000_000, OwnerCount = 1, Sequence = 10 },
            },
            Offers = new[]
            {
                new DexOffer
                {
                    Index = Hashes.HashOfferId(Alice, 5u),
                    Account = Alice,
                    TakerPays = pays,
                    TakerGets = Drops(5_000_000),
                    Quality = XrplQuality.FromAmounts(pays, Drops(5_000_000)),
                },
            },
        };

        // OfferSequence cancels the book's only offer, and the new one rests in the same book.
        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, new OfferCreate
        {
            Account = Alice,
            TakerPays = pays.ToCurrency(),
            TakerGets = Drops(6_000_000).ToCurrency(),
            Fee = new Currency { Value = "10" },
            Sequence = 10,
            OfferSequence = 5,
        });

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        DexSnapshot after = result.SnapshotAfter;
        CollectionAssert.AreEqual(new[] { Hashes.HashOfferId(Alice, 10u) }, after.Offers.Select(o => o.Index).ToArray());
        Assert.AreEqual(1u, AccountOf(after, Alice).OwnerCount, "one offer cancelled, one placed");
        AssertSameIndex(new DexIndex(after), DexIndex.Of(after), "replaced");
    }

    [TestMethod]
    public void AnAccountAFailedPaymentWouldHaveCreatedIsNotWrittenBack()
    {
        DexSnapshot snapshot = Build(gateways: 1, owners: 10, offersPerBook: 5);

        PaymentFlowResult failed = PaymentFlow.Evaluate(snapshot, Pay(Alice, Carol, Drops(2_000_000_000_000)));

        Assert.AreEqual("tecUNFUNDED_PAYMENT", failed.EngineResult);
        Assert.IsNull(AccountOf(failed.SnapshotAfter, Carol));
        Assert.AreEqual("tecNO_DST_INSUF_XRP", PaymentFlow.Evaluate(failed.SnapshotAfter, Pay(Alice, Carol, Drops(5))).EngineResult);
    }

    [TestMethod]
    public void APoolsListedAccountFollowsItsXrp()
    {
        DexSnapshot built = Build(gateways: 1, owners: 10, offersPerBook: 5, withPool: true);
        DexSnapshot snapshot = With(built, accounts: built.Accounts.Append(new DexAccount { Address = Pool, Balance = 10_000_000_000 }).ToList());

        PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, Pay(Alice, Bob, Iou(Gateway(0), "250"), Drops(1_000_000_000_000)));

        DexSnapshot after = result.SnapshotAfter;
        Assert.IsTrue(result.Pools.Count > 0, "the payment moves the pool");
        Assert.AreEqual(after.Pools.Single().Balance.StMantissa, AccountOf(after, Pool).Balance);
        AssertSameIndex(new DexIndex(after), DexIndex.Of(after), "pool account");
    }

    /// <summary>
    /// The roll copies these types property by property; one added to them must be copied there
    /// too, which this names so it cannot be forgotten.
    /// </summary>
    [TestMethod]
    public void TheEntriesTheRollCopiesHaveTheProperties()
    {
        AssertProperties(typeof(DexSnapshot), "ParentCloseTime", "LedgerSequence", "ReserveBase", "ReserveIncrement", "Accounts", "TrustLines",
            "Offers", "Pools", "MptIssuances", "MptHoldings", "Domains", "Credentials", "PartialBooks", "ApproximatedOwners");
        AssertProperties(typeof(DexAccount), "Address", "Balance", "OwnerCount", "Sequence", "DefaultRipple", "DisallowIncomingTrustline", "TransferRate",
            "TickSize", "GlobalFreeze", "RequireAuth", "RequireDestinationTag", "DepositAuth", "DepositPreauthorized", "DepositPreauthorizedCredentials");
        AssertProperties(typeof(DexOffer), "Index", "Account", "TakerPays", "TakerGets", "Quality", "Expiration", "DomainId", "Hybrid");
        AssertProperties(typeof(DexAmmPool), "Account", "Balance", "Balance2", "AssetFrozen", "Asset2Frozen", "TradingFee", "DiscountedFee",
            "SlotAccount", "SlotAuthAccounts", "SlotExpiration");
        AssertProperties(typeof(DexMptIssuance), "MptIssuanceId", "Issuer", "OutstandingAmount", "MaximumAmount", "TransferFee", "CanTrade",
            "CanTransfer", "Locked", "RequireAuth", "DomainId");
        AssertProperties(typeof(DexMptHolding), "Account", "MptIssuanceId", "Amount", "Locked", "Authorized");
        AssertProperties(typeof(DexTrustLine), "Account", "Balance", "Limit", "PeerLimit", "QualityIn", "QualityOut", "PeerQualityIn", "PeerQualityOut",
            "NoRipple", "PeerNoRipple", "Frozen", "FrozenByAccount", "DeepFrozen", "Authorized", "PeerAuthorized", "Reserve", "PeerReserve");
    }

    private static void AssertProperties(Type type, params string[] expected) =>
        CollectionAssert.AreEquivalent(expected, type.GetProperties().Where(p => p.CanWrite).Select(p => p.Name).ToArray(), type.Name);

    private static List<object> RunChain(DexSnapshot snapshot, IReadOnlyList<(string Name, Func<DexSnapshot, object> Evaluate)> chain)
    {
        List<object> results = new List<object>();
        foreach ((string name, Func<DexSnapshot, object> evaluate) in chain)
        {
            object result = evaluate(snapshot);
            Assert.AreEqual(DexResultText.Describe(evaluate(Fresh(snapshot))), DexResultText.Describe(result), $"{name}: through the derived index");

            DexSnapshot after = result is PaymentFlowResult payment ? payment.SnapshotAfter : ((OfferCrossingResult)result).SnapshotAfter;
            AssertSameIndex(new DexIndex(after), DexIndex.Of(after), name);
            if (IsApplied(result))
                AssertAgreesWithResult(snapshot, after, result, name);

            results.Add(result);
            snapshot = after;
        }

        return results;
    }

    private static bool IsApplied(object result) =>
        result is PaymentFlowResult payment ? payment.Applied : ((OfferCrossingResult)result).Applied;

    private static (IReadOnlyList<BalanceChange> Balances, IReadOnlyList<OfferChange> Offers, IReadOnlyList<TrustLineChange> Lines, IReadOnlyList<AmmPoolChange> Pools) Changes(object result) =>
        result switch
        {
            PaymentFlowResult p => (p.BalanceChanges, p.Offers, p.TrustLines, p.Pools),
            OfferCrossingResult o => (o.BalanceChanges, o.Offers, o.TrustLines, o.Pools),
            _ => throw new ArgumentException("Not a result.", nameof(result)),
        };

    /// <summary>The snapshot after must hold what the result says changed, and nothing else in XRP.</summary>
    private static void AssertAgreesWithResult(DexSnapshot before, DexSnapshot after, object result, string step)
    {
        (IReadOnlyList<BalanceChange> balances, IReadOnlyList<OfferChange> offers, IReadOnlyList<TrustLineChange> lines, IReadOnlyList<AmmPoolChange> pools) = Changes(result);

        foreach (BalanceChange change in balances)
        {
            XrplAmount moved = XrplAmountMath.ExactDifference(BalanceOf(after, change.Account, change.Change.Asset), BalanceOf(before, change.Account, change.Change.Asset));
            Assert.AreEqual(change.Change.ToString(), moved.ToString(), $"{step}: {change.Account} {change.Change.Asset.Currency ?? change.Change.Asset.MptIssuanceId}");
        }

        HashSet<string> xrpMoved = new HashSet<string>(balances.Where(b => b.Change.Kind == AmountKind.Xrp).Select(b => b.Account));
        foreach (DexAccount account in after.Accounts)
        {
            if (!xrpMoved.Contains(account.Address))
                Assert.AreEqual(AccountOf(before, account.Address)?.Balance ?? 0, account.Balance, $"{step}: {account.Address} XRP unchanged");
        }

        foreach (OfferChange change in offers)
        {
            DexOffer offer = after.Offers.FirstOrDefault(o => o.Index == change.Index);
            if (change.Deleted)
            {
                Assert.IsNull(offer, $"{step}: offer {change.Index} removed");
                continue;
            }

            Assert.AreEqual(change.TakerPays, offer.TakerPays, $"{step}: offer {change.Index} TakerPays");
            Assert.AreEqual(change.TakerGets, offer.TakerGets, $"{step}: offer {change.Index} TakerGets");
        }

        foreach (TrustLineChange change in lines)
            Assert.AreEqual(change.Created, LineOf(after, change.Account, change.Peer, change.Currency) != null, $"{step}: line {change.Account}/{change.Peer}");

        foreach (AmmPoolChange change in pools)
            Assert.AreEqual(DexResultText.Describe(change), DexResultText.Describe(PoolChangeOf(after, change)), $"{step}: pool {change.Account}");
    }

    private static AmmPoolChange PoolChangeOf(DexSnapshot snapshot, AmmPoolChange change)
    {
        DexAmmPool pool = snapshot.Pools.Single(p => p.Account == change.Account);
        return new AmmPoolChange(pool.Account, pool.Balance, pool.Balance2);
    }

    private static DexAccount AccountOf(DexSnapshot snapshot, string address) =>
        snapshot.Accounts.FirstOrDefault(a => a.Address == address);

    private static DexTrustLine LineOf(DexSnapshot snapshot, string a, string b, string currency) =>
        snapshot.TrustLines.FirstOrDefault(l => l.Balance.Asset.Currency == currency &&
            ((l.Account == a && l.Balance.Asset.Issuer == b) || (l.Account == b && l.Balance.Asset.Issuer == a)));

    /// <summary>What <paramref name="account"/> holds of <paramref name="asset"/> in the snapshot, as a balance change reads it.</summary>
    private static XrplAmount BalanceOf(DexSnapshot snapshot, string account, IssuedCurrency asset)
    {
        switch (XrplAmount.KindOf(asset))
        {
            case AmountKind.Xrp:
                return Drops(AccountOf(snapshot, account)?.Balance ?? 0);
            case AmountKind.Mpt:
                string id = asset.MptIssuanceId.ToUpperInvariant();
                if (account == DexAssets.MptIssuer(id))
                {
                    ulong outstanding = snapshot.MptIssuances.Single(i => i.MptIssuanceId.ToUpperInvariant() == id).OutstandingAmount;
                    return XrplAmount.Parse(asset, "-" + outstanding.ToString(CultureInfo.InvariantCulture));
                }

                ulong held = snapshot.MptHoldings.FirstOrDefault(h => h.Account == account && h.MptIssuanceId.ToUpperInvariant() == id)?.Amount ?? 0;
                return XrplAmount.Parse(asset, held.ToString(CultureInfo.InvariantCulture));
            default:
                DexTrustLine line = LineOf(snapshot, account, asset.Issuer, asset.Currency);
                if (line == null)
                    return XrplAmount.Parse(asset, "0");

                return line.Account == account ? line.Balance.WithAssetForTest(asset) : (-line.Balance).WithAssetForTest(asset);
        }
    }

    private static void AssertSameIndex(DexIndex expected, DexIndex actual, string step)
    {
        Assert.AreEqual(Text(expected.Accounts, DexResultText.Describe), Text(actual.Accounts, DexResultText.Describe), $"{step}: accounts");
        Assert.AreEqual(Text(expected.Lines, LineText), Text(actual.Lines, LineText), $"{step}: lines");
        Assert.AreEqual(Text(expected.Offers, DexResultText.Describe), Text(actual.Offers, DexResultText.Describe), $"{step}: offers");
        Assert.AreEqual(BooksText(expected), BooksText(actual), $"{step}: books");
        Assert.AreEqual(DexResultText.Describe(expected.Pools), DexResultText.Describe(actual.Pools), $"{step}: pools");
        Assert.AreEqual(string.Join(",", expected.PoolAccounts.OrderBy(a => a, StringComparer.Ordinal)),
            string.Join(",", actual.PoolAccounts.OrderBy(a => a, StringComparer.Ordinal)), $"{step}: pool accounts");
        Assert.AreEqual(Text(expected.MptIssuances, DexResultText.Describe), Text(actual.MptIssuances, DexResultText.Describe), $"{step}: issuances");
        Assert.AreEqual(Text(expected.MptHoldings, DexResultText.Describe), Text(actual.MptHoldings, DexResultText.Describe), $"{step}: MPTokens");
        Assert.AreEqual(expected.Domains.Count, actual.Domains.Count, $"{step}: domains");
        Assert.AreEqual(expected.Credentials.Count, actual.Credentials.Count, $"{step}: credentials");
        Assert.AreEqual(expected.PartialBooks.Count, actual.PartialBooks.Count, $"{step}: partial books");
    }

    private static string Text<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> map, Func<TValue, string> describe) =>
        string.Join("\n", map.Select(e => e.Key + " => " + describe(e.Value)).OrderBy(line => line, StringComparer.Ordinal));

    private static string BooksText(DexIndex index) =>
        string.Join("\n", index.BookKeys
            .Select(book => book + ": " + string.Join(",", index.Book(book).Select(o => o.Index)))
            .OrderBy(line => line, StringComparer.Ordinal));

    private static string AmountText(XrplAmount? amount) =>
        amount is { } value ? $"{value} {value.Asset.Currency}/{value.Asset.Issuer}/{value.Asset.MptIssuanceId}" : "null";

    private static string SideText(LineSide side) =>
        $"{AmountText(side.Limit)} {side.QualityIn} {side.QualityOut} {side.NoRipple} {side.Freeze} {side.Auth} {side.Reserve}";

    private static string LineText(LineInfo line) =>
        $"{line.Key} [{SideText(line.First)}] [{SideText(line.Second)}] {line.DeepFrozen} {AmountText(line.Balance)}";

    /// <summary>The same entries under a new instance, which gets an index built afresh.</summary>
    private static DexSnapshot Fresh(DexSnapshot snapshot) => With(snapshot);

    private static DexSnapshot With(DexSnapshot snapshot, IReadOnlyList<DexAccount> accounts = null) =>
        new DexSnapshot
        {
            ParentCloseTime = snapshot.ParentCloseTime,
            LedgerSequence = snapshot.LedgerSequence,
            ReserveBase = snapshot.ReserveBase,
            ReserveIncrement = snapshot.ReserveIncrement,
            Accounts = accounts ?? snapshot.Accounts,
            TrustLines = snapshot.TrustLines,
            Offers = snapshot.Offers,
            Pools = snapshot.Pools,
            MptIssuances = snapshot.MptIssuances,
            MptHoldings = snapshot.MptHoldings,
            Domains = snapshot.Domains,
            Credentials = snapshot.Credentials,
            PartialBooks = snapshot.PartialBooks,
            ApproximatedOwners = snapshot.ApproximatedOwners,
        };
}
