using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Integration tests verifying AMM behavior with MPT (Multi-Purpose Token) assets.
/// As of rippled 3.1.3, AMM does not support MPT — these tests verify the expected rejection.
/// DEX/AMM support for MPT is proposed in XLS-62: https://github.com/XRPLF/XRPL-Standards/discussions/231
/// </summary>
[TestClass]
[TestCategory("AMM")]
public class TestIAMMCreateMpt
{
    public TestContext TestContext { get; set; }

    private static IXrplClient client;
    private static readonly TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static void AssertSuccess(TransactionSummary res, string context)
    {
        string result = res.Meta?.TransactionResult;
        Assert.IsTrue(
            result is "tesSUCCESS" or "terQUEUED",
            $"{context} failed: {result}");
    }

    [TestMethod]
    public async Task TestAMMCreate_MptXrpPool_RejectedByProtocol()
    {
        // rippled 3.1.3 does not support MPT in AMM pools.
        // AMMCreate with MPT Amount is rejected with "Amount can not be MPT" local check.
        // This test documents and verifies this protocol-level restriction.
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder);

        // Create MPT issuance
        MPTokenIssuanceCreate mptCreateTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        mptCreateTx = await client.Autofill(mptCreateTx);
        TransactionSummary mptResult = await client.SubmitAndWait(mptCreateTx, walletIssuer, true);
        AssertSuccess(mptResult, "MPTokenIssuanceCreate");

        string issuanceId = mptResult.Meta?.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be present in metadata");

        // Authorize and fund holder
        MPTokenAuthorize authTx = new MPTokenAuthorize
        {
            Account = walletHolder.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        TransactionSummary authResult = await client.SubmitAndWait(authTx, walletHolder, true);
        AssertSuccess(authResult, "MPTokenAuthorize");

        Payment paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                Value = "10000",
                MPTokenIssuanceID = issuanceId,
            },
        };
        paymentTx = await client.Autofill(paymentTx);
        TransactionSummary payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        AssertSuccess(payResult, "MPT Payment");

        // Attempt to create AMM pool with MPT — should be rejected
        AMMCreate ammCreate = new AMMCreate
        {
            Account = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                Value = "1000",
                MPTokenIssuanceID = issuanceId,
            },
            Amount2 = new Currency { ValueAsXrp = 10m },
            TradingFee = 500,
        };
        ITransactionRequest autofilled = await client.Autofill(ammCreate);

        try
        {
            await client.SubmitAndWait(autofilled, walletHolder, true);
            Assert.Fail("Expected RippleException: AMM does not support MPT assets");
        }
        catch (RippledException ex)
        {
            // rippled rejects with "Amount can not be MPT" local check
            Assert.IsTrue(
                ex.Message.Contains("MPT", StringComparison.OrdinalIgnoreCase),
                $"Expected MPT-related rejection, got: {ex.Message}");
        }
    }
}
