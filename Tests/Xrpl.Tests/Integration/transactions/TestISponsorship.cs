using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("Sponsorship")]
public class TestISponsorship
{
    private static bool sponsorAmendmentActive;

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
        sponsorAmendmentActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.Sponsor);
    }

    [TestInitialize]
    public void CheckSponsorAmendment()
    {
        if (!sponsorAmendmentActive)
        {
            Assert.Inconclusive("Sponsor amendment (XLS-68) is not enabled on the test node; bump the nightly in .ci-config/Dockerfile.nightly and uncomment Sponsor in rippled.batchv11.cfg to run these tests.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
    }

    private static async Task<LOSponsorship> GetSponsorshipObject(string ownerAddress)
    {
        AccountObjectsRequest request = new AccountObjectsRequest(ownerAddress)
        {
            Type = LedgerEntryType.Sponsorship,
        };
        AccountObjects response = await client.AccountObjects(request);

        return response?.AccountObjectList?
            .OfType<LOSponsorship>()
            .FirstOrDefault();
    }

    [TestMethod]
    public async Task TestSponsorshipSet_BySponsor_CreatesLedgerObject()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);

        SponsorshipSet tx = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmount = new Currency { ValueAsXrp = 5m },
            RemainingOwnerCount = 3,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, sponsor, true);
        ValidateResult(result);

        LOSponsorship sponsorship = await GetSponsorshipObject(sponsor.ClassicAddress);
        Assert.IsNotNull(sponsorship, "Sponsorship ledger object should exist after SponsorshipSet");
        Assert.AreEqual(sponsor.ClassicAddress, sponsorship.Owner);
        Assert.AreEqual(sponsee.ClassicAddress, sponsorship.Sponsee);
        Assert.AreEqual((uint)3, sponsorship.RemainingOwnerCount);
    }

    [TestMethod]
    public async Task TestSponsoredPayment_SponsorPaysFee()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        XrplWallet destination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee, destination);

        // Establish sponsorship
        SponsorshipSet setup = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmount = new Currency { ValueAsXrp = 5m },
        };
        setup = await client.Autofill(setup);
        ValidateResult(await client.SubmitAndWait(setup, sponsor, true));

        // Sponsee sends a payment whose fee is covered by the sponsor;
        // the sponsor co-signs via SponsorSignature.
        Payment payment = new Payment
        {
            Account = sponsee.ClassicAddress,
            Destination = destination.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1m },
            Sponsor = sponsor.ClassicAddress,
            SponsorFlags = SponsorCoverage.spfSponsorFee,
        };
        payment = await client.Autofill(payment);

        System.Text.Json.Nodes.JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponsee);
        SignatureResult signed = SponsorSigningHelper.SignSponsored(prepared, sponsee, sponsor);

        Submit response = await client.SubmitRequest(signed.TxBlob, true);
        if (response is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"Sponsored payment failed: {response.EngineResult}");
    }

    [TestMethod]
    public async Task TestSponsorshipSet_DeleteObject()
    {
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet sponsee = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor, sponsee);

        SponsorshipSet create = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmount = new Currency { ValueAsXrp = 2m },
        };
        create = await client.Autofill(create);
        ValidateResult(await client.SubmitAndWait(create, sponsor, true));
        Assert.IsNotNull(await GetSponsorshipObject(sponsor.ClassicAddress));

        SponsorshipSet delete = new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            Flags = (uint)SponsorshipSetFlags.tfDeleteObject,
        };
        delete = await client.Autofill(delete);
        ValidateResult(await client.SubmitAndWait(delete, sponsor, true));

        Assert.IsNull(await GetSponsorshipObject(sponsor.ClassicAddress),
            "Sponsorship ledger object should be removed after tfDeleteObject");
    }
}
