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
/// With the featureMPTokensV2 amendment enabled, AMM supports MPT assets (XLS-62);
/// these tests verify that creating an MPT/XRP AMM pool succeeds.
/// XLS-62: https://github.com/XRPLF/XRPL-Standards/discussions/231
/// NOTE: featureMPTokensV2 is In Development (not yet on Mainnet); the CI standalone node
/// enables it via rippled.cfg [features] to exercise this path ahead of release.
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
    public async Task TestAMMCreate_MptXrpPool_Succeeds()
    {
        // With featureMPTokensV2 enabled, an MPT/XRP AMM pool can be created (XLS-62).
        // Set up an MPT, fund a holder, then create the AMM and assert it succeeds.
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder);

        // Create MPT issuance
        MPTokenIssuanceCreate mptCreateTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            // MPT must be tradable (lsfMPTCanTrade) for AMM/DEX use and transferable to move into the pool
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTrade | MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
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

        // Create AMM pool with an MPT + XRP — succeeds when featureMPTokensV2 is enabled
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

        TransactionSummary ammResult = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(ammResult, "AMMCreate MPT/XRP pool");
    }
}
