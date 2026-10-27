using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// ConfidentialMPTMirrorUpdate and ConfidentialMPTHolderKeyUpdate (ConfidentialMPTKeyRotation)
/// against a node.
/// </summary>
/// <remarks>
/// As in <see cref="TestIConfidentialMPT"/>, a positive path needs ciphertexts and proofs from an
/// external prover the SDK does not have. Each case is instead built so that the node's answer
/// depends on a field arriving intact: rippled checks the fields in a fixed order, and a field
/// lost in serialization would stop it at an earlier check with a different code.
/// ConfidentialMPTKeyRotation is Supported::No on rippled develop: the class runs on the nightly
/// stand with the amendment in <c>[features]</c> and is inconclusive everywhere else.
/// </remarks>
[TestClass]
[TestCategory("ConfidentialMPT")]
public class TestIConfidentialMPTKeyRotation
{
    private const string Amendment = "ConfidentialMPTKeyRotation";

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [TestInitialize]
    public async Task RequireKeyRotationAsync()
    {
        await AmendmentGuard.RequireTransactionTypeAsync(client, "ConfidentialMPTHolderKeyUpdate", Amendment);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestHolderKeyUpdate_CancelRecovery_ReachesTheHolderMPToken()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);
        string issuanceId = await CreateConfidentialIssuanceAsync(issuer);

        // No MPToken yet: preclaim finds the issuance by MPTokenIssuanceID, then misses the holder's token.
        Assert.AreEqual("tecOBJECT_NOT_FOUND", await SubmitCancelRecoveryAsync(holder, issuanceId));

        TransactionSummary authorized = await client.SubmitAndWait(new MPTokenAuthorize
        {
            Account = holder.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        }, holder, true, true);
        Assert.AreEqual("tesSUCCESS", authorized.Meta.TransactionResult);

        // The token exists but holds no confidential balance, so there is nothing to recover.
        Assert.AreEqual("tecNO_PERMISSION", await SubmitCancelRecoveryAsync(holder, issuanceId));
    }

    [TestMethod]
    public async Task TestMirrorUpdate_HolderMode_ChecksTheAuditorCiphertext()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);
        string issuanceId = ParseMPTID.GenerateMPTokenIssuanceID(1, issuer.ClassicAddress);

        // preflight reads the issuer out of MPTokenIssuanceID, accepts the holder as submitter
        // because Holder is absent, then rejects the ciphertext's length. Losing
        // AuditorEncryptedAmount would end at "no ciphertext" (temMALFORMED) instead.
        ConfidentialMPTMirrorUpdate update = await client.Autofill(new ConfidentialMPTMirrorUpdate
        {
            Account = holder.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            AuditorEncryptedAmount = new string('B', 128),
            ZKProof = new string('E', 128),
        });
        Submit response = await client.SubmitRequest(holder.Sign(update).TxBlob, false);
        AmendmentGuard.SkipIfDisabled(response.EngineResult, Amendment);
        Assert.AreEqual("temBAD_CIPHERTEXT", response.EngineResult, response.EngineResultMessage);
    }

    [TestMethod]
    public async Task TestMirrorUpdate_IssuerMode_RefusesTheIssuerAsHolder()
    {
        XrplWallet issuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer);
        string issuanceId = ParseMPTID.GenerateMPTokenIssuanceID(1, issuer.ClassicAddress);

        // Holder present selects issuer mode; naming the issuer itself is malformed. Without Holder
        // the issuer would fail the opposite check, so the code proves Holder reached the node.
        ConfidentialMPTMirrorUpdate update = await client.Autofill(new ConfidentialMPTMirrorUpdate
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = issuer.ClassicAddress,
            IssuerEncryptedAmount = new string('C', 128),
            ZKProof = new string('E', 128),
        });
        Submit response = await client.SubmitRequest(issuer.Sign(update).TxBlob, false);
        AmendmentGuard.SkipIfDisabled(response.EngineResult, Amendment);
        Assert.AreEqual("temMALFORMED", response.EngineResult, response.EngineResultMessage);
    }

    private static async Task<string> CreateConfidentialIssuanceAsync(XrplWallet issuer)
    {
        MPTokenIssuanceCreate issuance = await client.Autofill(new MPTokenIssuanceCreate
        {
            Account = issuer.ClassicAddress,
            MaximumAmount = "1000000",
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer | MPTokenIssuanceCreateFlags.tfMPTCanHoldConfidentialBalance,
        });
        uint sequence = issuance.Sequence!.Value;
        TransactionSummary created = await client.SubmitAndWait(issuance, issuer, true);
        Assert.AreEqual("tesSUCCESS", created.Meta.TransactionResult);
        return ParseMPTID.GenerateMPTokenIssuanceID(sequence, issuer.ClassicAddress);
    }

    private static async Task<string> SubmitCancelRecoveryAsync(XrplWallet holder, string issuanceId)
    {
        ConfidentialMPTHolderKeyUpdate cancel = await client.Autofill(new ConfidentialMPTHolderKeyUpdate
        {
            Account = holder.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = ConfidentialMPTHolderKeyUpdateFlags.tfCancelRecovery,
        });
        Submit response = await client.SubmitRequest(holder.Sign(cancel).TxBlob, false);
        AmendmentGuard.SkipIfDisabled(response.EngineResult, Amendment);
        return response.EngineResult;
    }
}
