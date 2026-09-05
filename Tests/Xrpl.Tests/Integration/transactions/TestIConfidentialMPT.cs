using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// ConfidentialTransfer serialization e2e check.
/// Producing VALID encrypted amounts and ZK proofs requires an external prover the
/// SDK does not implement, so a positive-path test is not possible here. Instead this
/// submits a ConfidentialMPTConvert with well-formed but cryptographically bogus
/// payloads and asserts the node answers with a DOMAIN error (tem/tec from the
/// ConfidentialTransfer logic), not a serialization error — proving the binary
/// encoding of the new transaction type is parsed correctly by rippled.
/// </summary>
[TestClass]
[TestCategory("ConfidentialMPT")]
public class TestIConfidentialMPT
{
    private static bool confidentialActive;

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
        confidentialActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.ConfidentialTransfer);
    }

    [TestInitialize]
    public void CheckAmendment()
    {
        if (!confidentialActive)
        {
            Assert.Inconclusive("ConfidentialTransfer amendment is not enabled on the test node; run the nightly stand (.ci-config/docker-compose.batchv11.yml).");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestConfidentialMPTConvert_BogusProof_RejectedByDomainLogicNotParser()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);

        // Real MPT issuance so the transaction reaches the ConfidentialTransfer logic
        MPTokenIssuanceCreate issuance = new MPTokenIssuanceCreate
        {
            Account = issuer.ClassicAddress,
            MaximumAmount = "1000000",
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        issuance = await client.Autofill(issuance);
        uint issuanceSequence = issuance.Sequence!.Value;
        TransactionSummary created = await client.SubmitAndWait(issuance, issuer, true);
        Assert.AreEqual("tesSUCCESS", created.Meta.TransactionResult);

        string issuanceId = global::Xrpl.Utils.ParseMPTID.GenerateMPTokenIssuanceID(issuanceSequence, issuer.ClassicAddress);

        ConfidentialMPTConvert convert = new ConfidentialMPTConvert
        {
            Account = issuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            MPTAmount = "100",
            HolderEncryptionKey = new string('A', 66),
            HolderEncryptedAmount = new string('B', 128),
            IssuerEncryptedAmount = new string('C', 128),
            BlindingFactor = new string('D', 64),
            ZKProof = new string('E', 128),
        };
        convert = await client.Autofill(convert);

        SignatureResult signed = issuer.Sign(convert.ToDictionary());
        Submit response = await client.SubmitRequest(signed.TxBlob, false);

        // Domain-level rejection proves the node successfully parsed our binary encoding.
        // Serialization failures surface as invalidTransaction, and tel/ter/tef admission
        // results would not prove the payload reached the ConfidentialTransfer logic.
        Assert.IsTrue(response.EngineResult.StartsWith("tem", System.StringComparison.Ordinal)
                   || response.EngineResult.StartsWith("tec", System.StringComparison.Ordinal),
            $"Expected a tem/tec verdict from ConfidentialTransfer domain logic, got: {response.EngineResult}");
        TestContext.WriteLine($"Node verdict for bogus confidential payload: {response.EngineResult} — {response.EngineResultMessage}");
    }
}
