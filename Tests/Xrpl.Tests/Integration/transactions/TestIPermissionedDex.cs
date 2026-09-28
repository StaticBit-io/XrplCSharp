using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.ClientLib.Integration.BookCrossingHarness;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// The permissioned DEX and credentials against the node: offers and payments in a domain walk
/// the domain's book - its offers and the hybrid ones, never the pool - members only; an owner
/// whose credential expired loses its domain offers; and a payment presenting credentials meets
/// deposit authorization. Each is compared with what the node does.
/// </summary>
[TestClass]
public class TestIPermissionedDex
{
    private const string Kyc = "KYC";

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

    private static OfferCreate Offer(XrplWallet taker, XrplAmount takerPays, XrplAmount takerGets, string domainId = null, OfferCreateFlags flags = 0) => new OfferCreate
    {
        Account = taker.ClassicAddress,
        TakerPays = takerPays.ToCurrency(),
        TakerGets = takerGets.ToCurrency(),
        DomainID = domainId,
        Flags = flags == 0 ? null : flags,
    };

    private static Payment Pay(XrplWallet from, XrplWallet to, XrplAmount amount) => new Payment
    {
        Account = from.ClassicAddress,
        Destination = to.ClassicAddress,
        Amount = amount.ToCurrency(),
    };

    /// <summary>An issuer of USD, a credential issuer, a domain accepting its KYC credential, and the members given it.</summary>
    private static async Task<(IssuedCurrency Usd, string Domain)> Market(XrplWallet gw, XrplWallet credentials, params XrplWallet[] members)
    {
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        string domain = await dex.Domain(credentials, (credentials, Kyc));
        foreach (XrplWallet member in members)
            await dex.Credential(credentials, member, Kyc);

        return (usd, domain);
    }

    [TestMethod]
    public async Task DomainOfferCrossesOnlyTheDomainBook()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet gw = w[0], issuer = w[1], alice = w[2], bob = w[3], carol = w[4];
        (IssuedCurrency usd, string domain) = await Market(gw, issuer, alice, bob);
        await dex.Trusts((alice, usd), (bob, usd), (carol, usd));
        await dex.Pays((gw, bob, Amount(usd, "100")), (gw, carol, Amount(usd, "100")));

        // carol's open offer is better, but a domain offer sees only bob's, in the domain.
        await dex.Offer(carol, Amount(usd, "10"), Drops(8_000_000));
        await dex.Offer(bob, Amount(usd, "10"), Drops(10_000_000), domainId: domain);

        OfferCrossingResult result = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "5"), Drops(5_000_000), domain));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsTrue(result.Offers.All(o => o.Account == bob.ClassicAddress), "only the domain's offer is crossed");
    }

    [TestMethod]
    public async Task HybridOfferServesBothBooks()
    {
        XrplWallet[] w = await dex.Wallets(5);
        XrplWallet gw = w[0], issuer = w[1], alice = w[2], bob = w[3], outsider = w[4];
        (IssuedCurrency usd, string domain) = await Market(gw, issuer, alice, bob);
        await dex.Trusts((alice, usd), (bob, usd), (outsider, usd));
        await dex.Pays((gw, bob, Amount(usd, "100")));
        await dex.Offer(bob, Amount(usd, "20"), Drops(20_000_000), OfferCreateFlags.tfHybrid, domainId: domain);

        // An outsider crosses it in the open book, a member in the domain.
        OfferCrossingResult open = await dex.CrossAndCompare(outsider, Offer(outsider, Amount(usd, "5"), Drops(5_000_000)));
        Assert.AreEqual("tesSUCCESS", open.EngineResult);
        Assert.IsNotEmpty(open.Offers);

        OfferCrossingResult member = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "5"), Drops(5_000_000), domain));
        Assert.AreEqual("tesSUCCESS", member.EngineResult);
        Assert.IsNotEmpty(member.Offers);
    }

    [TestMethod]
    public async Task OutsidersAreRefused()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], issuer = w[1], alice = w[2], outsider = w[3];
        (IssuedCurrency usd, string domain) = await Market(gw, issuer, alice);
        await dex.Trusts((alice, usd), (outsider, usd));
        await dex.Pays((gw, outsider, Amount(usd, "10")));

        OfferCrossingResult offer = await dex.CrossAndCompare(outsider, Offer(outsider, Drops(1_000_000), Amount(usd, "1"), domain));
        Assert.AreEqual("tecNO_PERMISSION", offer.EngineResult);

        Payment payment = Pay(outsider, alice, Amount(usd, "1"));
        payment.DomainID = domain;
        PaymentFlowResult paid = await dex.PayAndCompare(outsider, payment);
        Assert.AreEqual("tecNO_PERMISSION", paid.EngineResult);

        // A credential not yet accepted does not make a member.
        await dex.Credential(issuer, outsider, Kyc, accept: false);
        OfferCrossingResult pending = await dex.CrossAndCompare(outsider, Offer(outsider, Drops(1_000_000), Amount(usd, "1"), domain));
        Assert.AreEqual("tecNO_PERMISSION", pending.EngineResult);
    }

    [TestMethod]
    public async Task ExpiredCredentialRemovesTheOwnersDomainOffer()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], issuer = w[1], alice = w[2], bob = w[3];
        (IssuedCurrency usd, string domain) = await Market(gw, issuer, alice);
        await dex.Trusts((alice, usd), (bob, usd));
        await dex.Pays((gw, bob, Amount(usd, "100")));

        uint expiration = await dex.LastCloseTime() + 20;
        await dex.Credential(issuer, bob, Kyc, expiration);
        await dex.Offer(bob, Amount(usd, "10"), Drops(10_000_000), domainId: domain);
        await dex.WaitForCloseAfter(expiration + 1);

        // bob's credential has expired: his offer is removed from the domain book, not crossed.
        OfferCrossingResult result = await dex.CrossAndCompare(alice, Offer(alice, Amount(usd, "5"), Drops(5_000_000), domain));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsTrue(result.Offers.Single(o => o.Account == bob.ClassicAddress).Deleted);
        Assert.IsNotNull(result.PlacedTakerPays, "nothing crossed, alice's offer rests");

        // bob himself is refused, his expired credential with him.
        OfferCrossingResult own = await dex.CrossAndCompare(bob, Offer(bob, Drops(1_000_000), Amount(usd, "1"), domain));
        Assert.AreEqual("tecEXPIRED", own.EngineResult);
    }

    [TestMethod]
    public async Task PaymentThroughTheDomainBook()
    {
        XrplWallet[] w = await dex.Wallets(6);
        XrplWallet gw = w[0], issuer = w[1], sender = w[2], receiver = w[3], maker = w[4], openMaker = w[5];
        (IssuedCurrency usd, string domain) = await Market(gw, issuer, sender, receiver, maker);
        await dex.Trusts((receiver, usd), (maker, usd), (openMaker, usd));
        await dex.Pays((gw, maker, Amount(usd, "100")), (gw, openMaker, Amount(usd, "100")));
        await dex.Offer(openMaker, Amount(usd, "20"), Drops(10_000_000));
        await dex.Offer(maker, Amount(usd, "20"), Drops(20_000_000), domainId: domain);

        Payment payment = Pay(sender, receiver, Amount(usd, "8"));
        payment.SendMax = Drops(10_000_000).ToCurrency();
        payment.DomainID = domain;
        PaymentFlowResult result = await dex.PayAndCompare(sender, payment);
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.IsTrue(result.Offers.All(o => o.Account == maker.ClassicAddress), "only the domain's offer is taken");
    }

    [TestMethod]
    public async Task CredentialsMeetDepositAuthorization()
    {
        XrplWallet[] w = await dex.Wallets(4);
        XrplWallet gw = w[0], issuer = w[1], sender = w[2], receiver = w[3];
        IssuedCurrency usd = Iou("USD", gw);
        await dex.Issuer(gw);
        await dex.Trusts((sender, usd), (receiver, usd));
        await dex.Pays((gw, sender, Amount(usd, "50")));
        await dex.SetFlag(receiver, AccountSetAsfFlags.asfDepositAuth);
        await dex.Submit(
            new DepositPreauth
            {
                Account = receiver.ClassicAddress,
                AuthorizeCredentials = new List<AuthorizeCredentialEntry>
                {
                    new AuthorizeCredentialEntry { Credential = new AuthorizeCredentialBody { Issuer = issuer.ClassicAddress, CredentialType = Kyc } },
                },
            },
            receiver);

        // No credential: refused. A credential not accepted: bad. Accepted: through.
        PaymentFlowResult refused = await dex.PayAndCompare(sender, Pay(sender, receiver, Amount(usd, "1")));
        Assert.AreEqual("tecNO_PERMISSION", refused.EngineResult);

        await dex.Credential(issuer, sender, Kyc, accept: false);
        Payment presenting = Pay(sender, receiver, Amount(usd, "1"));
        presenting.CredentialIDs = new List<string> { CredentialId(sender, issuer, Kyc) };
        PaymentFlowResult bad = await dex.PayAndCompare(sender, presenting);
        Assert.AreEqual("tecBAD_CREDENTIALS", bad.EngineResult);

        await dex.Submit(new CredentialAccept { Account = sender.ClassicAddress, Issuer = issuer.ClassicAddress, CredentialType = Kyc }, sender);
        Payment accepted = Pay(sender, receiver, Amount(usd, "1"));
        accepted.CredentialIDs = new List<string> { CredentialId(sender, issuer, Kyc) };
        PaymentFlowResult through = await dex.PayAndCompare(sender, accepted);
        Assert.AreEqual("tesSUCCESS", through.EngineResult);
    }
}
