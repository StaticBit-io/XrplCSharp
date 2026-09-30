using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// The engine's checks around the flow itself: an <c>OfferCreate</c>'s <c>preflight</c> and
/// <c>preclaim</c>, <c>OfferSequence</c>, owners the issuer does not authorize, trust lines a
/// balance back at zero releases or deletes, permissioned domains and hybrid offers, credentials
/// a payment presents, partially read books, and the book directory keys a deep read walks.
/// </summary>
[TestClass]
public class TestUDexGaps
{
    private const uint Now = 800_000_000;
    private const string Kyc = "4B5943";

    // Real addresses: credentials are keyed by the hash of their accounts.
    private static readonly string Gw = XrplWallet.Generate().ClassicAddress;
    private static readonly string Alice = XrplWallet.Generate().ClassicAddress;
    private static readonly string Bob = XrplWallet.Generate().ClassicAddress;
    private static readonly string Carol = XrplWallet.Generate().ClassicAddress;
    private static readonly string CredentialIssuer = XrplWallet.Generate().ClassicAddress;
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = Gw };
    private static readonly string DomainId = new string('A', 64);

    private static XrplAmount Dollars(string value) => XrplAmount.Parse(Usd, value);

    private static XrplAmount Drops(long drops) => XrplAmount.Parse(Xrp, drops.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static DexAccount Account(string address, uint ownerCount = 0, bool requireAuth = false, bool globalFreeze = false, uint sequence = 10) =>
        new DexAccount
        {
            Address = address,
            Balance = 1_000_000_000,
            OwnerCount = ownerCount,
            Sequence = sequence,
            RequireAuth = requireAuth,
            GlobalFreeze = globalFreeze,
        };

    private static DexTrustLine Line(string holder, string balance, string limit = "1000", bool authorized = false, bool reserve = true, bool deepFrozen = false) =>
        new DexTrustLine
        {
            Account = holder,
            Balance = Dollars(balance),
            Limit = limit == null ? null : Dollars(limit),
            PeerAuthorized = authorized,
            Reserve = reserve,
            DeepFrozen = deepFrozen,
        };

    private static DexOffer Offer(string index, string owner, XrplAmount takerPays, XrplAmount takerGets, string domain = null, bool hybrid = false) =>
        new DexOffer
        {
            Index = index,
            Account = owner,
            TakerPays = takerPays,
            TakerGets = takerGets,
            Quality = XrplQuality.FromAmounts(takerPays, takerGets),
            DomainId = domain,
            Hybrid = hybrid,
        };

    private static DexSnapshot Snapshot(
        IEnumerable<DexAccount> accounts,
        IEnumerable<DexTrustLine> lines,
        IEnumerable<DexOffer> offers = null,
        IEnumerable<DexDomain> domains = null,
        IEnumerable<DexCredential> credentials = null,
        IEnumerable<DexBook> partialBooks = null) =>
        new DexSnapshot
        {
            ParentCloseTime = Now,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = accounts.ToList(),
            TrustLines = lines.ToList(),
            Offers = (offers ?? Array.Empty<DexOffer>()).ToList(),
            Domains = (domains ?? Array.Empty<DexDomain>()).ToList(),
            Credentials = (credentials ?? Array.Empty<DexCredential>()).ToList(),
            PartialBooks = (partialBooks ?? Array.Empty<DexBook>()).ToList(),
        };

    private static OfferCreate Create(string account, XrplAmount takerPays, XrplAmount takerGets, OfferCreateFlags flags = 0) =>
        new OfferCreate
        {
            Account = account,
            TakerPays = takerPays.ToCurrency(),
            TakerGets = takerGets.ToCurrency(),
            Flags = flags == 0 ? null : flags,
            Fee = new Currency { Value = "10" },
            Sequence = 10,
        };

    private static Payment Pay(string from, string to, XrplAmount amount) =>
        new Payment { Account = from, Destination = to, Amount = amount.ToCurrency(), Fee = new Currency { Value = "10" } };

    private static DexDomain Domain() =>
        new DexDomain
        {
            DomainId = DomainId,
            Owner = CredentialIssuer,
            AcceptedCredentials = new[] { new DexCredentialType { Issuer = CredentialIssuer, CredentialType = Kyc } },
        };

    private static DexCredential Credential(string subject, bool accepted = true, uint? expiration = null) =>
        new DexCredential { Subject = subject, Issuer = CredentialIssuer, CredentialType = Kyc, Accepted = accepted, Expiration = expiration };

    // ---- OfferCreate's own checks ----

    [TestMethod]
    [DataRow(OfferCreateFlags.tfImmediateOrCancel | OfferCreateFlags.tfFillOrKill, "temINVALID_FLAG")]
    [DataRow(OfferCreateFlags.tfHybrid, "temINVALID_FLAG")]
    public void MalformedFlags(OfferCreateFlags flags, string expected)
    {
        DexSnapshot snapshot = Snapshot(new[] { Account(Gw), Account(Alice) }, Array.Empty<DexTrustLine>());
        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("1"), Drops(1_000_000), flags));
        Assert.AreEqual(expected, result.EngineResult);
        Assert.IsFalse(result.Applied);
        Assert.IsEmpty(result.BalanceChanges);
    }

    [TestMethod]
    public void MalformedOffers()
    {
        DexSnapshot snapshot = Snapshot(new[] { Account(Gw), Account(Alice) }, Array.Empty<DexTrustLine>());

        OfferCreate zeroSequence = Create(Alice, Dollars("1"), Drops(1_000_000));
        zeroSequence.OfferSequence = 0;
        Assert.AreEqual("temBAD_SEQUENCE", OfferCreateCrossing.Cross(snapshot, zeroSequence).EngineResult);

        OfferCreate zeroExpiration = Create(Alice, Dollars("1"), Drops(1_000_000));
        zeroExpiration.Expiration = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual("temBAD_EXPIRATION", OfferCreateCrossing.Cross(snapshot, zeroExpiration).EngineResult);

        Assert.AreEqual("temBAD_OFFER", OfferCreateCrossing.Cross(snapshot, Create(Alice, Drops(1), Drops(2))).EngineResult);
        Assert.AreEqual("temREDUNDANT", OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("1"), Dollars("2"))).EngineResult);

        IssuedCurrency badXrp = new IssuedCurrency { Currency = "XRP", Issuer = Gw };
        OfferCreate bad = Create(Alice, Dollars("1"), Drops(1_000_000));
        bad.TakerPays = new Currency { CurrencyCode = "XRP", Issuer = badXrp.Issuer, Value = "1" };
        Assert.AreEqual("temBAD_CURRENCY", OfferCreateCrossing.Cross(snapshot, bad).EngineResult);
    }

    [TestMethod]
    public void RefusedBeforeCrossing()
    {
        DexOffer maker = Offer("A1", Bob, Drops(1_000_000), Dollars("1"));

        // The issuer froze everything.
        DexSnapshot frozen = Snapshot(new[] { Account(Gw, globalFreeze: true), Account(Alice, 1), Account(Bob, 2) }, new[] { Line(Alice, "0"), Line(Bob, "10") }, new[] { maker });
        OfferCrossingResult result = OfferCreateCrossing.Cross(frozen, Create(Alice, Dollars("1"), Drops(1_000_000)));
        Assert.AreEqual("tecFROZEN", result.EngineResult);
        Assert.IsTrue(result.Applied);
        Assert.HasCount(1, result.BalanceChanges, "the fee only");

        // Nothing to give.
        DexSnapshot empty = Snapshot(new[] { Account(Gw), Account(Alice, 1), Account(Bob, 2) }, new[] { Line(Alice, "0"), Line(Bob, "10") }, new[] { maker });
        Assert.AreEqual("tecUNFUNDED_OFFER", OfferCreateCrossing.Cross(empty, Create(Alice, Drops(1_000_000), Dollars("1"))).EngineResult);

        // Expired: the parent ledger closed at or after the expiration.
        OfferCreate expired = Create(Alice, Dollars("1"), Drops(1_000_000));
        expired.Expiration = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Now);
        Assert.AreEqual("tecEXPIRED", OfferCreateCrossing.Cross(empty, expired).EngineResult);

        // An OfferSequence not below the account's sequence.
        OfferCreate ahead = Create(Alice, Dollars("1"), Drops(1_000_000));
        ahead.OfferSequence = 10;
        Assert.AreEqual("temBAD_SEQUENCE", OfferCreateCrossing.Cross(empty, ahead).EngineResult);

        // No issuer, and a deep-frozen line.
        OfferCreate unknownIssuer = Create(Alice, XrplAmount.Parse(new IssuedCurrency { Currency = "EUR", Issuer = Carol }, "1"), Drops(1_000_000));
        Assert.AreEqual("tecNO_ISSUER", OfferCreateCrossing.Cross(empty, unknownIssuer).EngineResult);
        DexSnapshot deep = Snapshot(new[] { Account(Gw), Account(Alice, 1), Account(Bob, 2) }, new[] { Line(Alice, "0", deepFrozen: true), Line(Bob, "10") }, new[] { maker });
        Assert.AreEqual("tecFROZEN", OfferCreateCrossing.Cross(deep, Create(Alice, Dollars("1"), Drops(1_000_000))).EngineResult);
    }

    [TestMethod]
    public void RequireAuthRefusesAndRemoves()
    {
        // Offer_test's testMissingAuth, first step: alice asked for USD before gw required
        // authorization; bob, authorized, crosses her offer - which is deleted, not crossed.
        DexOffer alices = Offer("A1", Alice, Dollars("40"), Drops(4_000_000_000));
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw, 1, requireAuth: true), Account(Alice, 1), Account(Bob, 1) },
            new[] { Line(Bob, "50", "100", authorized: true) },
            new[] { alices });

        OfferCrossingResult bob = OfferCreateCrossing.Cross(snapshot, Create(Bob, Drops(4_000_000_000), Dollars("40")));
        Assert.AreEqual("tesSUCCESS", bob.EngineResult);
        Assert.HasCount(1, bob.Offers);
        Assert.IsTrue(bob.Offers[0].Deleted);
        Assert.AreEqual(OfferRemovalReason.Unauthorized, bob.Offers[0].Reason);
        Assert.IsTrue(bob.Offers[0].FilledTakerPays.IsZero && bob.Offers[0].FilledTakerGets.IsZero, "nothing taken of it");
        Assert.IsEmpty(bob.Fills);
        Assert.AreEqual(Dollars("40"), bob.PlacedTakerGets, "bob's offer is placed whole");

        // alice cannot ask for USD: no line, then a line gw has not authorized.
        Assert.AreEqual("tecNO_LINE", OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("40"), Drops(4_000_000_000))).EngineResult);
        DexSnapshot unauthorized = Snapshot(
            new[] { Account(Gw, 2, requireAuth: true), Account(Alice, 1), Account(Bob, 1) },
            new[] { Line(Bob, "50", "100", authorized: true), Line(Alice, "0", "0") });
        Assert.AreEqual("tecNO_AUTH", OfferCreateCrossing.Cross(unauthorized, Create(Alice, Dollars("40"), Drops(4_000_000_000))).EngineResult);
    }

    [TestMethod]
    public void OfferSequenceCancelsFirst()
    {
        // alice's own resting offer, the one OfferSequence 5 names, is cancelled before crossing.
        string cancelled = Hashes.HashOfferId(Alice, 5);
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice, 2), Account(Bob, 2) },
            new[] { Line(Alice, "10"), Line(Bob, "10") },
            new[]
            {
                Offer(cancelled, Alice, Drops(9_000_000), Dollars("9")),
                Offer("B1", Bob, Drops(1_000_000), Dollars("1")),
            });

        OfferCreate replacing = Create(Alice, Dollars("1"), Drops(1_000_000));
        replacing.OfferSequence = 5;
        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, replacing);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(OfferRemovalReason.Cancelled, result.Offers.Single(o => o.Index == cancelled).Reason);
        OfferChange taken = result.Offers.Single(o => o.Index == "B1");
        Assert.AreEqual(OfferRemovalReason.Consumed, taken.Reason);
        Assert.AreEqual(Drops(1_000_000), taken.FilledTakerPays);
        Assert.AreEqual(Dollars("1"), taken.FilledTakerGets);
    }

    [TestMethod]
    public void RemovedOffersSayWhyAndFillsRunInOrder()
    {
        // The book, best first: an expired offer, an unfunded one, alice's own at the same
        // price as her new one, then two funded ones - the first taken whole, the second in part.
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice, 1), Account(Bob, 3), Account(Carol, 2) },
            new[] { Line(Alice, "0"), Line(Bob, "5"), Line(Carol, "0") },
            new[]
            {
                new DexOffer
                {
                    Index = "E1", Account = Bob, TakerPays = Drops(900_000), TakerGets = Dollars("1"),
                    Quality = XrplQuality.FromAmounts(Drops(900_000), Dollars("1")), Expiration = Now,
                },
                Offer("U1", Carol, Drops(950_000), Dollars("1")),
                Offer("B1", Bob, Drops(1_000_000), Dollars("1")),
                Offer("B2", Bob, Drops(2_200_000), Dollars("2")),
            });

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("2"), Drops(2_200_000)));

        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(OfferRemovalReason.Expired, result.Offers.Single(o => o.Index == "E1").Reason);
        Assert.AreEqual(OfferRemovalReason.Unfunded, result.Offers.Single(o => o.Index == "U1").Reason);
        Assert.AreEqual(OfferRemovalReason.Consumed, result.Offers.Single(o => o.Index == "B1").Reason);

        OfferChange partial = result.Offers.Single(o => o.Index == "B2");
        Assert.IsFalse(partial.Deleted);
        Assert.IsNull(partial.Reason);
        Assert.AreEqual(Dollars("1"), partial.FilledTakerGets);
        Assert.AreEqual(Drops(1_100_000), partial.FilledTakerPays);

        // B1 at the better quality in the first pass, then B2.
        Assert.HasCount(2, result.Fills);
        Assert.AreEqual("B1", result.Fills[0].OfferIndex);
        Assert.AreEqual("B2", result.Fills[1].OfferIndex);
        Assert.IsTrue(result.Fills[0].Pass < result.Fills[1].Pass, "one quality per pass");
        Assert.IsTrue(result.Fills.All(f => f.Strand == 0 && !f.IsPool && f.Owner == Bob));
        Assert.AreEqual(Dollars("1"), result.Fills[0].Out);
        Assert.AreEqual(Drops(1_000_000), result.Fills[0].In);
        Assert.IsTrue(result.Passes.Where(p => p.Outcome == FlowPassOutcome.Taken).Select(p => p.Pass).SequenceEqual(result.Fills.Select(f => f.Pass)));
    }

    [TestMethod]
    public void AnEmptyBookIsADryPass()
    {
        DexSnapshot snapshot = Snapshot(new[] { Account(Gw), Account(Alice, 1) }, new[] { Line(Alice, "0") });

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("1"), Drops(1_000_000)));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(FlowPassOutcome.Dry, result.Passes.Single().Outcome, "no liquidity, not liquidity out of reach");
        Assert.IsEmpty(result.Fills);
    }

    [TestMethod]
    public void OwnOfferAtTheSamePriceIsSelfCrossed()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice, 2) },
            new[] { Line(Alice, "10") },
            new[] { Offer("A1", Alice, Drops(1_000_000), Dollars("1")) });

        // alice buys USD at the price she sells it: her own offer is deleted, not crossed.
        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("1"), Drops(1_000_000)));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(OfferRemovalReason.SelfCrossed, result.Offers.Single().Reason);
        Assert.IsEmpty(result.Fills);
    }

    // ---- trust lines back at zero ----

    [TestMethod]
    public void ALineAtItsDefaultsIsDeletedWhenItEmpties()
    {
        // alice's line holds nothing but her balance, which she pays back to gw. Without
        // DefaultRipple, NoRipple set is her side's default, as trustCreate leaves it.
        DexTrustLine created = new DexTrustLine { Account = Alice, Balance = Dollars("10"), Reserve = true, NoRipple = true };
        DexSnapshot defaults = Snapshot(new[] { Account(Gw), Account(Alice, 1) }, new[] { created });
        PaymentFlowResult deleted = PaymentFlow.Evaluate(defaults, Pay(Alice, Gw, Dollars("10")));
        Assert.AreEqual("tesSUCCESS", deleted.EngineResult);
        Assert.HasCount(1, deleted.TrustLines);
        Assert.IsTrue(deleted.TrustLines[0].Deleted);

        // A limit keeps it; so does a peer paying its reserve.
        DexTrustLine withLimit = new DexTrustLine { Account = Alice, Balance = Dollars("10"), Limit = Dollars("100"), Reserve = true, NoRipple = true };
        DexSnapshot limited = Snapshot(new[] { Account(Gw), Account(Alice, 1) }, new[] { withLimit });
        Assert.IsEmpty(PaymentFlow.Evaluate(limited, Pay(Alice, Gw, Dollars("10"))).TrustLines);

        DexTrustLine peerReserve = new DexTrustLine { Account = Alice, Balance = Dollars("10"), Reserve = true, NoRipple = true, PeerReserve = true };
        DexSnapshot both = Snapshot(new[] { Account(Gw, 1), Account(Alice, 1) }, new[] { peerReserve });
        Assert.IsEmpty(PaymentFlow.Evaluate(both, Pay(Alice, Gw, Dollars("10"))).TrustLines);

        // Paying only part keeps it too.
        Assert.IsEmpty(PaymentFlow.Evaluate(defaults, Pay(Alice, Gw, Dollars("4"))).TrustLines);
    }

    [TestMethod]
    public void CrossingCreatesTheBuyersLine()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice), Account(Bob, 2) },
            new[] { Line(Bob, "10") },
            new[] { Offer("B1", Bob, Drops(1_000_000), Dollars("1")) });

        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("1"), Drops(1_000_000)));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.HasCount(1, result.TrustLines);
        Assert.IsTrue(result.TrustLines[0].Created);
        Assert.AreEqual("USD", result.TrustLines[0].Currency);
    }

    // ---- permissioned domains ----

    [TestMethod]
    public void ADomainOfferCrossesOnlyTheDomainBook()
    {
        // carol's open offer is the best; bob's domain offer and dan's hybrid one are in the domain.
        string dan = XrplWallet.Generate().ClassicAddress;
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice, 1), Account(Bob, 2), Account(Carol, 2), Account(dan, 2), Account(CredentialIssuer) },
            new[] { Line(Alice, "0"), Line(Bob, "10"), Line(Carol, "10"), Line(dan, "10") },
            new[]
            {
                Offer("C1", Carol, Drops(800_000), Dollars("1")),
                Offer("D1", dan, Drops(900_000), Dollars("1"), DomainId, hybrid: true),
                Offer("B1", Bob, Drops(1_000_000), Dollars("1"), DomainId),
            },
            new[] { Domain() },
            new[] { Credential(Alice), Credential(Bob), Credential(dan) });

        OfferCreate inDomain = Create(Alice, Dollars("2"), Drops(2_000_000));
        inDomain.DomainID = DomainId;
        OfferCrossingResult member = OfferCreateCrossing.Cross(snapshot, inDomain);
        Assert.AreEqual("tesSUCCESS", member.EngineResult);
        CollectionAssert.AreEquivalent(new[] { "D1", "B1" }, member.Offers.Select(o => o.Index).ToArray());

        // The open book holds carol's offer and the hybrid one.
        OfferCrossingResult open = OfferCreateCrossing.Cross(snapshot, Create(Alice, Dollars("2"), Drops(2_000_000)));
        Assert.AreEqual("tesSUCCESS", open.EngineResult);
        CollectionAssert.AreEquivalent(new[] { "C1", "D1" }, open.Offers.Select(o => o.Index).ToArray());

        // carol has no credential.
        OfferCreate outsider = Create(Carol, Drops(1_000_000), Dollars("1"));
        outsider.DomainID = DomainId;
        Assert.AreEqual("tecNO_PERMISSION", OfferCreateCrossing.Cross(snapshot, outsider).EngineResult);
    }

    [TestMethod]
    public void AnExpiredCredentialCostsTheDomainOffer()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice, 1), Account(Bob, 2), Account(CredentialIssuer) },
            new[] { Line(Alice, "0"), Line(Bob, "10") },
            new[] { Offer("B1", Bob, Drops(1_000_000), Dollars("1"), DomainId) },
            new[] { Domain() },
            new[] { Credential(Alice), Credential(Bob, expiration: Now - 1) });

        OfferCreate inDomain = Create(Alice, Dollars("1"), Drops(1_000_000));
        inDomain.DomainID = DomainId;
        OfferCrossingResult result = OfferCreateCrossing.Cross(snapshot, inDomain);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(OfferRemovalReason.NotInDomain, result.Offers.Single().Reason, "removed as out of the domain");
        Assert.IsNotNull(result.PlacedTakerPays, "nothing crossed");

        OfferCreate bobs = Create(Bob, Drops(1_000_000), Dollars("1"));
        bobs.DomainID = DomainId;
        Assert.AreEqual("tecEXPIRED", OfferCreateCrossing.Cross(snapshot, bobs).EngineResult);
    }

    [TestMethod]
    public void APaymentInADomainNeedsBothEndsInIt()
    {
        DexSnapshot snapshot = Snapshot(
            new[] { Account(Gw), Account(Alice, 1), Account(Bob, 1), Account(CredentialIssuer) },
            new[] { Line(Alice, "10"), Line(Bob, "0") },
            domains: new[] { Domain() },
            credentials: new[] { Credential(Alice) });

        Payment payment = Pay(Alice, Bob, Dollars("1"));
        payment.DomainID = DomainId;
        Assert.AreEqual("tecNO_PERMISSION", PaymentFlow.Evaluate(snapshot, payment).EngineResult);

        Payment zero = Pay(Alice, Bob, Dollars("1"));
        zero.DomainID = new string('0', 64);
        Assert.AreEqual("temMALFORMED", PaymentFlow.Evaluate(snapshot, zero).EngineResult);
    }

    // ---- credentials ----

    [TestMethod]
    public void CredentialsForDepositAuthorization()
    {
        DexAccount receiver = new DexAccount
        {
            Address = Bob,
            Balance = 1_000_000_000,
            OwnerCount = 2,
            DepositAuth = true,
            DepositPreauthorizedCredentials = new[] { new[] { new DexCredentialType { Issuer = CredentialIssuer, CredentialType = Kyc } } },
        };
        string id = Hashes.HashCredential(Alice, CredentialIssuer, Kyc);

        DexSnapshot Snap(DexCredential credential) => Snapshot(
            new[] { Account(Gw), Account(Alice, 1), receiver, Account(CredentialIssuer) },
            new[] { Line(Alice, "10"), Line(Bob, "0") },
            credentials: credential == null ? null : new[] { credential });

        Payment Presenting(params string[] ids)
        {
            Payment payment = Pay(Alice, Bob, Dollars("1"));
            payment.CredentialIDs = ids.ToList();
            return payment;
        }

        Assert.AreEqual("tecNO_PERMISSION", PaymentFlow.Evaluate(Snap(Credential(Alice)), Pay(Alice, Bob, Dollars("1"))).EngineResult);
        Assert.AreEqual("tesSUCCESS", PaymentFlow.Evaluate(Snap(Credential(Alice)), Presenting(id)).EngineResult);
        Assert.AreEqual("tecBAD_CREDENTIALS", PaymentFlow.Evaluate(Snap(null), Presenting(id)).EngineResult);
        Assert.AreEqual("tecBAD_CREDENTIALS", PaymentFlow.Evaluate(Snap(Credential(Alice, accepted: false)), Presenting(id)).EngineResult);
        Assert.AreEqual("tecEXPIRED", PaymentFlow.Evaluate(Snap(Credential(Alice, expiration: Now - 1)), Presenting(id)).EngineResult);
        Assert.AreEqual("temMALFORMED", PaymentFlow.Evaluate(Snap(Credential(Alice)), Presenting(id, id)).EngineResult);
        Assert.AreEqual("temMALFORMED", PaymentFlow.Evaluate(Snap(Credential(Alice)), Presenting()).EngineResult);
    }

    // ---- books read in part ----

    [TestMethod]
    public void RunningOffAPartialBookIsReported()
    {
        DexBook book = new DexBook { TakerPays = Xrp, TakerGets = Usd };
        DexSnapshot partial = Snapshot(
            new[] { Account(Gw), Account(Alice, 1), Account(Bob, 2) },
            new[] { Line(Alice, "0"), Line(Bob, "10") },
            new[] { Offer("B1", Bob, Drops(1_000_000), Dollars("1")), Offer("B2", Bob, Drops(1_000_000), Dollars("1")) },
            partialBooks: new[] { book });

        // The walk steps onto the offer after the last one it takes, which the node may remove,
        // so reaching the end of what was read is reported even when the request is met there.
        Assert.IsTrue(OfferCreateCrossing.Cross(partial, Create(Alice, Dollars("3"), Drops(3_000_000))).NeedsDeeperBooks, "the book ran out");
        Assert.IsTrue(OfferCreateCrossing.Cross(partial, Create(Alice, Dollars("2"), Drops(2_000_000))).NeedsDeeperBooks, "met on the last offer read");
        Assert.IsFalse(OfferCreateCrossing.Cross(partial, Create(Alice, Dollars("1"), Drops(1_000_000))).NeedsDeeperBooks, "an offer read is left");
    }

    [TestMethod]
    public void BookBaseMatchesTheLedgersDirectories()
    {
        // The book directories of XRP and Bitstamp's USD on mainnet, both ways.
        IssuedCurrency bitstamp = new IssuedCurrency { Currency = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B" };
        Assert.AreEqual("4627DFFCFF8B5A265EDBD8AE8C14A52325DBFEDAF4F5C32E0000000000000000", DexSnapshot.BookBase(Xrp, bitstamp, null));
        Assert.AreEqual("DFA3B6DDAB58C7E8E5D944E736DA4B7046C30E4F460FD9DE0000000000000000", DexSnapshot.BookBase(bitstamp, Xrp, null));
    }
}
