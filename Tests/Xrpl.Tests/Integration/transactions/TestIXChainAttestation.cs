using System.Collections.Generic;
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

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// The witness half of XLS-38 on one node. rippled resolves a bridge spec to the
/// locking-side Bridge entry first (readOrpeekBridge), so with only the locking door's
/// bridge on the ledger every bridge transaction is processed as the locking chain:
/// commits lock funds in the door, and an attestation that says the send happened on
/// the issuing chain (WasLockingChainSend = 0) releases them to the destination here.
/// That is enough to drive the whole flow - claim id, commit, witness attestation,
/// delivery or explicit claim, account creation - against a single standalone node.
/// </summary>
[TestClass]
[TestCategory("XChain")]
public class TestIXChainAttestation : TestIXChainBridgeBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    private static bool xchainEnabled;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
        xchainEnabled = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.XChainBridge);
    }

    [TestInitialize]
    public void CheckXChainBridgeAmendment()
    {
        if (!xchainEnabled)
        {
            Assert.Inconclusive("XChainBridge amendment is not enabled on the test node.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static Currency Drops(string drops) => new Currency { Value = drops, CurrencyCode = "XRP" };

    private static Currency Iou(string issuer, string value) => new Currency { CurrencyCode = TestCurrencyCode, Issuer = issuer, Value = value };

    private static async Task SubmitAsync(ITransactionRequest tx, XrplWallet signer)
    {
        ITransactionRequest autofilled = await client.Autofill(tx);
        ValidateResult(await client.SubmitAndWait(autofilled, signer, true));
    }

    private static async Task SetWitnessesAsync(XrplWallet door, uint quorum, params XrplWallet[] witnesses)
    {
        await SubmitAsync(new SignerListSet
        {
            Account = door.ClassicAddress,
            SignerQuorum = quorum,
            SignerEntries = witnesses
                .Select(w => new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = w.ClassicAddress, SignerWeight = 1 } })
                .ToList(),
        }, door);
    }

    /// <summary>Opens a fee sponsorship, and skips the test when XLS-68 is not on the node.</summary>
    private static async Task OpenSponsorshipAsync(XrplWallet sponsor, XrplWallet sponsee)
    {
        if (!await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.Sponsor))
        {
            Assert.Inconclusive("Sponsor amendment (XLS-68) is not enabled on the test node.");
        }

        await SubmitAsync(new SponsorshipSet
        {
            Account = sponsor.ClassicAddress,
            Sponsee = sponsee.ClassicAddress,
            FeeAmountDelta = new Currency { ValueAsXrp = 20m },
            RemainingOwnerCountDelta = 10,
        }, sponsor);
    }

    /// <summary>Stamps the sponsor onto the transaction and submits it with both signatures.</summary>
    private static async Task SubmitSponsoredAsync<T>(T tx, XrplWallet sponsee, XrplWallet sponsor)
        where T : TransactionRequest
    {
        tx.Sponsor = sponsor.ClassicAddress;
        tx.SponsorFlags = SponsorCoverage.spfSponsorFee;
        TransactionSummary res = await client.SubmitAndWaitSponsored(tx, sponsee, sponsor);
        Assert.IsTrue(res.Meta?.TransactionResult is "tesSUCCESS",
            $"sponsored {tx.TransactionType} must validate with tesSUCCESS, got {res.Meta?.TransactionResult}");
    }

    private static async Task<decimal> IouBalanceAsync(string holder, string issuer)
    {
        AccountLines lines = await client.AccountLines(new AccountLinesRequest(holder)).Typed();
        TrustLine line = lines.TrustLines?.FirstOrDefault(l => l.Account == issuer && l.Currency == TestCurrencyCode);
        return line?.BalanceAsNumber ?? 0m;
    }

    private static async Task<int> CountObjectsAsync(string account, LedgerEntryType type)
    {
        AccountObjects objects = await client.AccountObjects(new AccountObjectsRequest(account) { Type = type }).Typed();
        return objects.AccountObjectList?.Count ?? 0;
    }

    private sealed record IouBridge(XrplWallet Door, XrplWallet Issuer, XrplWallet Witness, XrplWallet User, XrplWallet Recipient, XChainBridgeModel Bridge);

    /// <summary>
    /// Locking door, IOU issuer, one witness, a user holding 1000 USD and a recipient
    /// with a trust line; bridge created on the door with the witness as its signer list;
    /// claim id 1 created by the recipient; 100 USD committed by the user.
    /// </summary>
    private static async Task<IouBridge> CommitOnIouBridgeAsync()
    {
        XrplWallet door = XrplWallet.Generate();
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet witness = XrplWallet.Generate();
        XrplWallet user = XrplWallet.Generate();
        XrplWallet recipient = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, door, issuer, witness, user, recipient);

        await EnableDefaultRipple(client, issuer);
        await SetupTrustLine(client, door, issuer.ClassicAddress);
        await SetupTrustLine(client, user, issuer.ClassicAddress);
        await SetupTrustLine(client, recipient, issuer.ClassicAddress);
        await SubmitAsync(new Payment { Account = issuer.ClassicAddress, Destination = user.ClassicAddress, Amount = Iou(issuer.ClassicAddress, "1000") }, issuer);

        // The issuing door only has to be an address in the spec: nothing on this ledger looks it up
        XChainBridgeModel bridge = CreateIouTestBridge(door.ClassicAddress, issuer.ClassicAddress, XrplWallet.Generate().ClassicAddress);
        await SubmitAsync(new XChainCreateBridge { Account = door.ClassicAddress, XChainBridge = bridge, SignatureReward = Drops("100") }, door);
        await SetWitnessesAsync(door, 1, witness);

        await SubmitAsync(new XChainCreateClaimID { Account = recipient.ClassicAddress, XChainBridge = bridge, SignatureReward = Drops("100"), OtherChainSource = user.ClassicAddress }, recipient);
        await SubmitAsync(new XChainCommit { Account = user.ClassicAddress, XChainBridge = bridge, XChainClaimID = "1", Amount = Iou(issuer.ClassicAddress, "100"), OtherChainDestination = recipient.ClassicAddress }, user);

        return new IouBridge(door, issuer, witness, user, recipient, bridge);
    }

    private static XChainAddClaimAttestation ClaimAttestation(IouBridge setup, string destination) => new XChainAddClaimAttestation
    {
        Account = setup.Witness.ClassicAddress,
        XChainBridge = setup.Bridge,
        OtherChainSource = setup.User.ClassicAddress,
        // The attested send is on the issuing chain, so the amount carries the issuing chain issue
        Amount = new Currency { CurrencyCode = TestCurrencyCode, Issuer = setup.Bridge.IssuingChainDoor, Value = "100" },
        AttestationRewardAccount = setup.Witness.ClassicAddress,
        Destination = destination,
        WasLockingChainSend = 0,
        XChainClaimID = "1",
    };

    [TestMethod]
    public async Task TestXChainAddClaimAttestation_WithDestination_DeliversOnQuorum()
    {
        IouBridge setup = await CommitOnIouBridgeAsync();
        Assert.AreEqual(1, await CountObjectsAsync(setup.Recipient.ClassicAddress, LedgerEntryType.XChainOwnedClaimID));

        XChainAddClaimAttestation attestation = XChainAttestationSigner.SignClaimAttestation(ClaimAttestation(setup, setup.Recipient.ClassicAddress), setup.Witness);
        Assert.IsTrue(XChainAttestationSigner.VerifyClaimAttestation(attestation), "the attestation must verify locally before submission");
        await SubmitAsync(attestation, setup.Witness);

        Assert.AreEqual(100m, await IouBalanceAsync(setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress), "quorum of one delivers the committed amount to the destination");
        Assert.AreEqual(0, await CountObjectsAsync(setup.Recipient.ClassicAddress, LedgerEntryType.XChainOwnedClaimID), "the claim id is consumed by the delivery");
    }

    [TestMethod]
    public async Task TestXChainClaim_AfterAttestationWithoutDestination_UsesDestinationTag()
    {
        IouBridge setup = await CommitOnIouBridgeAsync();

        XChainAddClaimAttestation attestation = XChainAttestationSigner.SignClaimAttestation(ClaimAttestation(setup, destination: null), setup.Witness);
        await SubmitAsync(attestation, setup.Witness);
        Assert.AreEqual(0m, await IouBalanceAsync(setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress), "without a destination the funds wait for XChainClaim");

        await SubmitAsync(new XChainClaim
        {
            Account = setup.Recipient.ClassicAddress,
            XChainBridge = setup.Bridge,
            XChainClaimID = "1",
            Destination = setup.Recipient.ClassicAddress,
            DestinationTag = 7,
            Amount = Iou(setup.Issuer.ClassicAddress, "100"),
        }, setup.Recipient);

        Assert.AreEqual(100m, await IouBalanceAsync(setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
        Assert.AreEqual(0, await CountObjectsAsync(setup.Recipient.ClassicAddress, LedgerEntryType.XChainOwnedClaimID));
    }

    [TestMethod]
    public async Task TestXChainAddClaimAttestation_UnlistedWitness_IsRejected()
    {
        IouBridge setup = await CommitOnIouBridgeAsync();
        XrplWallet stranger = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, stranger);

        XChainAddClaimAttestation attestation = ClaimAttestation(setup, setup.Recipient.ClassicAddress);
        attestation.Account = stranger.ClassicAddress;
        attestation.AttestationRewardAccount = stranger.ClassicAddress;
        XChainAttestationSigner.SignClaimAttestation(attestation, stranger);

        ITransactionRequest autofilled = await client.Autofill(attestation);
        TransactionFailedException ex = await Assert.ThrowsExactlyAsync<TransactionFailedException>(
            () => client.SubmitAndWait(autofilled, stranger, true));
        StringAssert.Contains(ex.Message, "tecNO_PERMISSION");
        Assert.AreEqual(0m, await IouBalanceAsync(setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
    }

    [TestMethod]
    public async Task TestXChainAddAccountCreateAttestation_TwoWitnesses_CreatesTheAccountOnQuorum()
    {
        XrplWallet door = XrplWallet.Generate();
        XrplWallet witness1 = XrplWallet.Generate();
        XrplWallet witness2 = XrplWallet.Generate();
        XrplWallet user = XrplWallet.Generate();
        XrplWallet created = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, door, witness1, witness2, user);

        XChainBridgeModel bridge = CreateXrpTestBridge(door.ClassicAddress);
        await SubmitAsync(new XChainCreateBridge { Account = door.ClassicAddress, XChainBridge = bridge, SignatureReward = Drops("100"), MinAccountCreateAmount = Drops("10000000") }, door);
        await SetWitnessesAsync(door, 2, witness1, witness2);

        await SubmitAsync(new XChainAccountCreateCommit
        {
            Account = user.ClassicAddress,
            XChainBridge = bridge,
            Destination = created.ClassicAddress,
            Amount = Drops("20000000"),
            SignatureReward = Drops("100"),
        }, user);

        XChainAddAccountCreateAttestation Attestation(XrplWallet witness) => XChainAttestationSigner.SignAccountCreateAttestation(new XChainAddAccountCreateAttestation
        {
            Account = witness.ClassicAddress,
            XChainBridge = bridge,
            XChainAccountCreateCount = "1",
            Amount = Drops("20000000"),
            SignatureReward = Drops("100"),
            OtherChainSource = user.ClassicAddress,
            Destination = created.ClassicAddress,
            AttestationRewardAccount = witness.ClassicAddress,
            WasLockingChainSend = 0,
        }, witness);

        // First attestation: below quorum, the door records it in an XChainOwnedCreateAccountClaimID
        await SubmitAsync(Attestation(witness1), witness1);
        Assert.AreEqual(1, await CountObjectsAsync(door.ClassicAddress, LedgerEntryType.XChainOwnedCreateAccountClaimID), "one attestation of two is parked on the door");
        await Assert.ThrowsExactlyAsync<RippledException>(() => client.AccountInfo(new AccountInfoRequest(created.ClassicAddress)).Typed(), "the account must not exist before quorum");

        // Second attestation: quorum reached, the account is created from the door's funds
        await SubmitAsync(Attestation(witness2), witness2);
        AccountInfo info = await client.AccountInfo(new AccountInfoRequest(created.ClassicAddress)).Typed();
        Assert.AreEqual("20000000", info.AccountData.Balance.Value, "the created account holds the committed amount");
        Assert.AreEqual(0, await CountObjectsAsync(door.ClassicAddress, LedgerEntryType.XChainOwnedCreateAccountClaimID), "the create-account claim id is consumed");
    }
    /// <summary>
    /// The witness half with the fee sponsored: an attestation is a transaction like any other,
    /// and a witness that does not hold XRP of its own is the obvious reason to sponsor one.
    /// </summary>
    [TestMethod]
    public async Task Sponsored_XChainAddClaimAttestation_And_XChainClaim()
    {
        IouBridge setup = await CommitOnIouBridgeAsync();
        XrplWallet sponsor = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sponsor);
        await OpenSponsorshipAsync(sponsor, setup.Witness);
        await OpenSponsorshipAsync(sponsor, setup.Recipient);

        // No Destination: the funds wait for an explicit claim, which is what makes the
        // sponsored XChainClaim below reachable in the same flow
        XChainAddClaimAttestation attestation =
            XChainAttestationSigner.SignClaimAttestation(ClaimAttestation(setup, destination: null), setup.Witness);
        await SubmitSponsoredAsync(attestation, setup.Witness, sponsor);
        Assert.AreEqual(0m, await IouBalanceAsync(setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));

        await SubmitSponsoredAsync(new XChainClaim
        {
            Account = setup.Recipient.ClassicAddress,
            XChainBridge = setup.Bridge,
            XChainClaimID = "1",
            Destination = setup.Recipient.ClassicAddress,
            Amount = Iou(setup.Issuer.ClassicAddress, "100"),
        }, setup.Recipient, sponsor);

        Assert.AreEqual(100m, await IouBalanceAsync(setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
        Assert.AreEqual(0, await CountObjectsAsync(setup.Recipient.ClassicAddress, LedgerEntryType.XChainOwnedClaimID));
    }

    [TestMethod]
    public async Task Sponsored_XChainAddAccountCreateAttestation()
    {
        XrplWallet door = XrplWallet.Generate();
        XrplWallet witness = XrplWallet.Generate();
        XrplWallet user = XrplWallet.Generate();
        XrplWallet sponsor = XrplWallet.Generate();
        XrplWallet created = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, door, witness, user, sponsor);

        XChainBridgeModel bridge = CreateXrpTestBridge(door.ClassicAddress);
        await SubmitAsync(new XChainCreateBridge { Account = door.ClassicAddress, XChainBridge = bridge, SignatureReward = Drops("100"), MinAccountCreateAmount = Drops("10000000") }, door);
        await SetWitnessesAsync(door, 1, witness);
        await SubmitAsync(new XChainAccountCreateCommit
        {
            Account = user.ClassicAddress,
            XChainBridge = bridge,
            Destination = created.ClassicAddress,
            Amount = Drops("20000000"),
            SignatureReward = Drops("100"),
        }, user);

        await OpenSponsorshipAsync(sponsor, witness);

        XChainAddAccountCreateAttestation attestation = XChainAttestationSigner.SignAccountCreateAttestation(new XChainAddAccountCreateAttestation
        {
            Account = witness.ClassicAddress,
            XChainBridge = bridge,
            XChainAccountCreateCount = "1",
            Amount = Drops("20000000"),
            SignatureReward = Drops("100"),
            OtherChainSource = user.ClassicAddress,
            Destination = created.ClassicAddress,
            AttestationRewardAccount = witness.ClassicAddress,
            WasLockingChainSend = 0,
        }, witness);

        await SubmitSponsoredAsync(attestation, witness, sponsor);

        AccountInfo info = await client.AccountInfo(new AccountInfoRequest(created.ClassicAddress)).Typed();
        Assert.AreEqual("20000000", info.AccountData.Balance.Value, "quorum of one creates the account from the door's funds");
    }
}
