using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// DynamicMPT (XLS-94) end-to-end coverage: capabilities and fields of an
/// issuance stay mutable unless the issuer freezes them via <c>ImmutableFlags</c>,
/// and a later MPTokenIssuanceSet either performs the mutation, enables a
/// capability through a tfMPTSet* flag, or freezes more of the issuance. Both
/// directions are checked against the ledger object, plus the permission rule
/// that makes the feature meaningful — mutating a frozen field is rejected.
///
/// Amendment-gated: DynamicMPT is Supported::No on rippled 3.2.x, so these
/// tests skip on the CI stand and run for real on the nightly stand, where
/// generate-amendments.sh puts DynamicMPT into [amendments].
/// </summary>
[TestClass]
[TestCategory("DynamicMPT")]
public class TestIDynamicMPT : TestIMPTokenBase
{
    private static IXrplClient client;
    private static bool dynamicMptActive;

    protected override IXrplClient GetClient() => client;

    /// <summary>"MPT-METADATA" in hex — the value the issuance is created with.</summary>
    private const string InitialMetadata = "4D50542D4D45544144415441";

    /// <summary>"MPT-UPDATED" in hex — the value a mutation writes over it.</summary>
    private const string UpdatedMetadata = "4D50542D55504441544544";

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
        dynamicMptActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.DynamicMPT);
    }

    [TestInitialize]
    public void CheckAmendment()
    {
        if (!dynamicMptActive)
        {
            Assert.Inconclusive("DynamicMPT amendment is not enabled on the test node; run the nightly stand (.ci-config/docker-compose.batchv11.yml).");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestDynamicMPT_ImmutableFlagsOnCreate_ReachTheLedgerObject()
    {
        XrplWallet issuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuer, nodeType);

        MPTokenIssuanceImmutableFlags immutable =
            MPTokenIssuanceImmutableFlags.tifMPTMetadata |
            MPTokenIssuanceImmutableFlags.tifMPTTransferFee |
            MPTokenIssuanceImmutableFlags.tifMPTCanLock;

        string issuanceId = await CreateIssuance(issuer, MPTokenIssuanceCreateFlags.tfMPTCanTransfer, immutable, InitialMetadata);

        LOMPTokenIssuance issuance = await ReadIssuance(issuanceId);

        Assert.IsNotNull(issuance.ImmutableFlags, "ImmutableFlags should be present on the issuance");
        Assert.AreEqual((uint)immutable, issuance.ImmutableFlags.Value, "ImmutableFlags should round-trip unchanged");
        Assert.AreEqual(InitialMetadata, issuance.MPTokenMetadata, "MPTokenMetadata should round-trip unchanged");
    }

    [TestMethod]
    public async Task TestDynamicMPT_MutateTransferFeeAndMetadata()
    {
        XrplWallet issuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuer, nodeType);

        // No ImmutableFlags: with DynamicMPT everything the amendment covers stays
        // mutable by default. A TransferFee above zero still requires lsfMPTCanTransfer
        // on the issuance (rippled MPTokenIssuanceSet::preclaim), hence tfMPTCanTransfer.
        string issuanceId = await CreateIssuance(
            issuer,
            MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
            null,
            InitialMetadata);

        MPTokenIssuanceSet mutation = new MPTokenIssuanceSet
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            TransferFee = 500,
            MPTokenMetadata = UpdatedMetadata,
        };
        mutation = await client.Autofill(mutation);
        TransactionSummary result = await client.SubmitAndWait(mutation, issuer, true);
        ValidateResult(result);

        LOMPTokenIssuance issuance = await ReadIssuance(issuanceId);

        Assert.AreEqual((ushort)500, issuance.TransferFee, "TransferFee should be the mutated value");
        Assert.AreEqual(UpdatedMetadata, issuance.MPTokenMetadata, "MPTokenMetadata should be the mutated value");

        // sfImmutableFlags is soeDEFAULT and this mutation never writes it, so the
        // issuance stays fully mutable
        Assert.IsTrue(
            issuance.ImmutableFlags is null or 0u,
            "ImmutableFlags should stay unset when the mutation does not freeze anything");
    }

    [TestMethod]
    public async Task TestDynamicMPT_SetCapabilityFlag_EnablesCapabilityOnTheIssuance()
    {
        XrplWallet issuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuer, nodeType);

        // Created WITHOUT tfMPTCanLock and without freezing it, so it may be enabled later
        string issuanceId = await CreateIssuance(issuer, null, null, null);

        LOMPTokenIssuance before = await ReadIssuance(issuanceId);
        Assert.IsTrue(
            (before.Flags.GetValueOrDefault() & MPTokenIssuanceFlags.MPTCanLock) == 0,
            "MPTCanLock should not be set before the mutation");

        MPTokenIssuanceSet enable = new MPTokenIssuanceSet
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTSetCanLock,
        };
        enable = await client.Autofill(enable);
        TransactionSummary result = await client.SubmitAndWait(enable, issuer, true);
        ValidateResult(result);

        LOMPTokenIssuance after = await ReadIssuance(issuanceId);
        Assert.IsTrue(
            (after.Flags.GetValueOrDefault() & MPTokenIssuanceFlags.MPTCanLock) != 0,
            "MPTCanLock should be set after tfMPTSetCanLock");
    }

    [TestMethod]
    public async Task TestDynamicMPT_MutationOfFrozenField_IsRejected()
    {
        XrplWallet issuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuer, nodeType);

        // Metadata frozen at creation: no later transaction may rewrite it
        string issuanceId = await CreateIssuance(
            issuer,
            null,
            MPTokenIssuanceImmutableFlags.tifMPTMetadata,
            InitialMetadata);

        MPTokenIssuanceSet mutation = new MPTokenIssuanceSet
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            MPTokenMetadata = UpdatedMetadata,
        };
        mutation = await client.Autofill(mutation);

        await Helper.ThrowsExceptionAsync<RippleException>(
            () => client.SubmitAndWait(mutation, issuer, true),
            "Final tx result is not success: tecNO_PERMISSION");

        LOMPTokenIssuance issuance = await ReadIssuance(issuanceId);
        Assert.AreEqual(InitialMetadata, issuance.MPTokenMetadata, "MPTokenMetadata should be untouched by the rejected mutation");
    }

    [TestMethod]
    public async Task TestDynamicMPT_FreezeViaSet_BlocksTheNextMutation()
    {
        XrplWallet issuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuer, nodeType);

        // Created fully mutable, then frozen by a separate MPTokenIssuanceSet:
        // doApply ORs ImmutableFlags into the ledger object, so a freeze is one-way
        string issuanceId = await CreateIssuance(issuer, null, null, InitialMetadata);

        MPTokenIssuanceSet freeze = new MPTokenIssuanceSet
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            ImmutableFlags = MPTokenIssuanceImmutableFlags.tifMPTMetadata,
        };
        freeze = await client.Autofill(freeze);
        ValidateResult(await client.SubmitAndWait(freeze, issuer, true));

        LOMPTokenIssuance frozen = await ReadIssuance(issuanceId);
        Assert.IsNotNull(frozen.ImmutableFlags, "ImmutableFlags should be present after the freeze");
        Assert.AreEqual(
            (uint)MPTokenIssuanceImmutableFlags.tifMPTMetadata,
            frozen.ImmutableFlags.Value,
            "ImmutableFlags should carry the freshly frozen bit");

        MPTokenIssuanceSet mutation = new MPTokenIssuanceSet
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            MPTokenMetadata = UpdatedMetadata,
        };
        mutation = await client.Autofill(mutation);

        await Helper.ThrowsExceptionAsync<RippleException>(
            () => client.SubmitAndWait(mutation, issuer, true),
            "Final tx result is not success: tecNO_PERMISSION");
    }

    private static async Task<string> CreateIssuance(
        XrplWallet issuer,
        MPTokenIssuanceCreateFlags? flags,
        MPTokenIssuanceImmutableFlags? immutableFlags,
        string metadata)
    {
        MPTokenIssuanceCreate create = new MPTokenIssuanceCreate
        {
            Account = issuer.ClassicAddress,
            Flags = flags,
            ImmutableFlags = immutableFlags,
            MPTokenMetadata = metadata,
        };
        create = await client.Autofill(create);
        TransactionSummary created = await client.SubmitAndWait(create, issuer, true);
        ValidateResult(created);

        string issuanceId = GetMPTokenIssuanceIdFromMeta(created);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be present in the metadata");
        return issuanceId;
    }

    private static async Task<LOMPTokenIssuance> ReadIssuance(string issuanceId)
    {
        LedgerEntryRequest request = new LedgerEntryRequest { MptIssuance = issuanceId };
        LedgerEntryResponse response = await client.LedgerEntry(request);

        Assert.IsNotNull(response?.Node, "ledger_entry should return the MPTokenIssuance node");
        Assert.IsInstanceOfType(response.Node, typeof(LOMPTokenIssuance), "Node should deserialize to LOMPTokenIssuance");
        return (LOMPTokenIssuance)response.Node;
    }
}
