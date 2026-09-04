using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// The refusals of XLS-38. Every case here builds a transaction the SDK is perfectly willing
/// to sign and submit, and lets the node say why it is wrong - which is the point: an
/// attestation is bytes this library produces, and the codes below are how rippled reports
/// that those bytes describe something other than what the ledger holds. A regression in
/// <see cref="XChainAttestationSigner"/> that still produces a well-formed signature would
/// pass the happy-path class in <see cref="TestIXChainAttestation"/> and fail here.
///
/// The stand is the same single node: rippled resolves a bridge spec to the locking-side
/// entry, so one door is enough to drive commit, attestation and claim.
/// </summary>
[TestClass]
[TestCategory("XChain")]
public class TestIXChainNegative : TestIXChainBridgeBase
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

    /// <summary>
    /// Submits a transaction that the ledger is expected to refuse, and asserts the code.
    /// A tec result reaches the ledger, so this is the result recorded there, not an
    /// engine opinion on the open ledger.
    /// </summary>
    private static async Task AssertRefusedAsync(ITransactionRequest tx, XrplWallet signer, string expectedCode)
    {
        ITransactionRequest autofilled = await client.Autofill(tx);
        TransactionFailedException ex = await Assert.ThrowsExactlyAsync<TransactionFailedException>(
            () => client.SubmitAndWait(autofilled, signer, true),
            $"{tx.TransactionType} must be refused with {expectedCode}");
        StringAssert.Contains(ex.Message, expectedCode);
    }

    /// <summary>
    /// The attested direction is part of the signed message. Saying the send happened on the
    /// locking chain, when this ledger is the locking chain, makes the destination chain the
    /// issuing one - which is not where the claim id lives.
    /// </summary>
    [TestMethod]
    public async Task AddClaimAttestation_WrongChainDirection_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client);

        XChainAddClaimAttestation attestation = ClaimAttestation(setup, setup.Recipient.ClassicAddress);
        attestation.WasLockingChainSend = 1;
        // The direction also picks the issue preflight expects on the attested amount
        // (attestationPreflight: bridgeSpec.issue(srcChain(wasLockingChainSend))), so the amount
        // has to move to the locking chain issue as well - otherwise this is refused as a
        // malformed proof before it ever reaches the check under test
        attestation.Amount = Iou(setup.Issuer.ClassicAddress, "100");
        XChainAttestationSigner.SignClaimAttestation(attestation, setup.Witness);

        await AssertRefusedAsync(attestation, setup.Witness, "tecXCHAIN_WRONG_CHAIN");
        Assert.AreEqual(0m, await IouBalanceAsync(client, setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
    }

    /// <summary>
    /// The claim id names the account that is allowed to send on the other chain. An
    /// attestation about anyone else is about a transfer this claim id does not cover.
    /// </summary>
    [TestMethod]
    public async Task AddClaimAttestation_SendingAccountMismatch_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client);

        XChainAddClaimAttestation attestation = ClaimAttestation(setup, setup.Recipient.ClassicAddress);
        attestation.OtherChainSource = XrplWallet.Generate().ClassicAddress;
        XChainAttestationSigner.SignClaimAttestation(attestation, setup.Witness);

        await AssertRefusedAsync(attestation, setup.Witness, "tecXCHAIN_SENDING_ACCOUNT_MISMATCH");
        Assert.AreEqual(0m, await IouBalanceAsync(client, setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
    }

    /// <summary>
    /// The signature verifies and the signer is on the door's list, but the key that produced
    /// it is neither the master nor the regular key of the account the attestation claims to
    /// speak for. rippled checks the pair, not just the signature.
    /// </summary>
    [TestMethod]
    public async Task AddClaimAttestation_KeyDoesNotBelongToTheSigner_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client);
        XrplWallet stranger = XrplWallet.Generate();

        XChainAddClaimAttestation attestation = ClaimAttestation(setup, setup.Recipient.ClassicAddress);
        // Set before signing: the signer only fills AttestationSignerAccount when it is empty,
        // so this keeps the listed witness as the claimed signer while the key is someone else's
        attestation.AttestationSignerAccount = setup.Witness.ClassicAddress;
        XChainAttestationSigner.SignClaimAttestation(attestation, stranger);
        Assert.IsTrue(XChainAttestationSigner.VerifyClaimAttestation(attestation),
            "the signature itself must be valid - the pair is what the node objects to");

        await AssertRefusedAsync(attestation, setup.Witness, "tecXCHAIN_BAD_PUBLIC_KEY_ACCOUNT_PAIR");
        Assert.AreEqual(0m, await IouBalanceAsync(client, setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
    }

    /// <summary>
    /// Two witnesses on a quorum of two, one attestation given: the attestation is recorded,
    /// but an explicit claim against it has nothing like enough weight behind it.
    /// </summary>
    [TestMethod]
    public async Task XChainClaim_BelowQuorum_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client, quorum: 2, witnessCount: 2);

        // No Destination: the funds wait for an explicit claim instead of being delivered
        XChainAddClaimAttestation attestation = ClaimAttestation(setup, null);
        XChainAttestationSigner.SignClaimAttestation(attestation, setup.Witness);
        await SubmitAsync(client, attestation, setup.Witness);

        await AssertRefusedAsync(new XChainClaim
        {
            Account = setup.Recipient.ClassicAddress,
            XChainBridge = setup.Bridge,
            XChainClaimID = "1",
            Destination = setup.Recipient.ClassicAddress,
            Amount = Iou(setup.Issuer.ClassicAddress, "100"),
        }, setup.Recipient, "tecXCHAIN_CLAIM_NO_QUORUM");

        Assert.AreEqual(0m, await IouBalanceAsync(client, setup.Recipient.ClassicAddress, setup.Issuer.ClassicAddress));
    }

    /// <summary>
    /// A claim id belongs to the account that created it. Another account cannot spend it,
    /// even with the attestations already in place.
    /// </summary>
    [TestMethod]
    public async Task XChainClaim_ByAnAccountThatDoesNotOwnTheClaimId_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client);
        XrplWallet stranger = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, stranger);

        XChainAddClaimAttestation attestation = ClaimAttestation(setup, null);
        XChainAttestationSigner.SignClaimAttestation(attestation, setup.Witness);
        await SubmitAsync(client, attestation, setup.Witness);

        await AssertRefusedAsync(new XChainClaim
        {
            Account = stranger.ClassicAddress,
            XChainBridge = setup.Bridge,
            XChainClaimID = "1",
            Destination = stranger.ClassicAddress,
            Amount = Iou(setup.Issuer.ClassicAddress, "100"),
        }, stranger, "tecXCHAIN_BAD_CLAIM_ID");
    }

    /// <summary>
    /// The reward a claim id offers has to be the one the bridge advertises; a claim id that
    /// paid less would underpay the witnesses that answer it.
    /// </summary>
    [TestMethod]
    public async Task XChainCreateClaimID_WithADifferentSignatureReward_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client);

        await AssertRefusedAsync(new XChainCreateClaimID
        {
            Account = setup.Recipient.ClassicAddress,
            XChainBridge = setup.Bridge,
            SignatureReward = Drops("99"),
            OtherChainSource = setup.User.ClassicAddress,
        }, setup.Recipient, "tecXCHAIN_REWARD_MISMATCH");
    }

    /// <summary>
    /// Delivery through a bridge is a payment and obeys the destination's rules: an account
    /// that requires a destination tag will not take an untagged claim.
    /// </summary>
    [TestMethod]
    public async Task XChainClaim_DestinationRequiresATag_IsRefused()
    {
        IouBridge setup = await CommitOnIouBridgeAsync(client);

        await SubmitAsync(client, new AccountSet
        {
            Account = setup.Recipient.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfRequireDest,
        }, setup.Recipient);

        XChainAddClaimAttestation attestation = ClaimAttestation(setup, null);
        XChainAttestationSigner.SignClaimAttestation(attestation, setup.Witness);
        await SubmitAsync(client, attestation, setup.Witness);

        await AssertRefusedAsync(new XChainClaim
        {
            Account = setup.Recipient.ClassicAddress,
            XChainBridge = setup.Bridge,
            XChainClaimID = "1",
            Destination = setup.Recipient.ClassicAddress,
            Amount = Iou(setup.Issuer.ClassicAddress, "100"),
        }, setup.Recipient, "tecDST_TAG_NEEDED");
    }

    /// <summary>
    /// A bridge that creates accounts sets the least it will create one with. Committing less
    /// than that would strand an account below the reserve on the other side.
    /// </summary>
    [TestMethod]
    public async Task XChainAccountCreateCommit_BelowTheMinimum_IsRefused()
    {
        XrplWallet door = XrplWallet.Generate();
        XrplWallet witness = XrplWallet.Generate();
        XrplWallet user = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, door, witness, user);

        XChainBridgeModel bridge = CreateXrpTestBridge(door.ClassicAddress);
        await SubmitAsync(client, new XChainCreateBridge
        {
            Account = door.ClassicAddress,
            XChainBridge = bridge,
            SignatureReward = Drops("100"),
            MinAccountCreateAmount = Drops("10000000"),
        }, door);
        await SetWitnessesAsync(client, door, 1, witness);

        await AssertRefusedAsync(new XChainAccountCreateCommit
        {
            Account = user.ClassicAddress,
            XChainBridge = bridge,
            Destination = XrplWallet.Generate().ClassicAddress,
            Amount = Drops("5000000"),
            SignatureReward = Drops("100"),
        }, user, "tecXCHAIN_INSUFF_CREATE_AMOUNT");
    }
}
