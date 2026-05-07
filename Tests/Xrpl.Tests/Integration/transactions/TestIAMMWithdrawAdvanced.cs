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

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIAMMWithdrawAdvanced : TestIAMMBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    /// <summary>
    /// Sole LP holder tries tfOneAssetWithdrawAll — protocol rejects with tecAMM_BALANCE.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_OneAssetWithdrawAll_SoleHolder_Fails()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse infoBefore = await GetAmmInfo();
        Console.WriteLine($"LP before: {infoBefore.Amm.LPTokenBalance.Value}");

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency { ValueAsXrp = 0 },
            Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);

        try
        {
            await client.SubmitAndWait(autofilled, walletHolder, true);
            Assert.Fail("Expected tecAMM_BALANCE for sole LP holder single-sided withdrawal");
        }
        catch (Exception ex) when (ex.Classify().RawErrorMessage!.Contains("tecAMM_BALANCE"))
        {
            Console.WriteLine($"Correctly rejected: {ex.Message}");
        }

        AMMInfoResponse infoAfter = await GetAmmInfo();
        Assert.IsNotNull(infoAfter.Amm, "Pool should still exist after failed withdrawal");
        Assert.AreEqual(
            infoBefore.Amm.LPTokenBalance.Value,
            infoAfter.Amm.LPTokenBalance.Value,
            "LP balance should be unchanged after tecAMM_BALANCE");
    }

    /// <summary>
    /// Two LP holders — first holder withdraws all LP into XRP via tfOneAssetWithdrawAll.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_OneAssetWithdrawAll_Xrp()
    {
        XrplWallet walletSecondHolder = await SetupSecondHolder();
        await CreatePool("1000", 10m);
        await DepositSecondHolder(walletSecondHolder, 0.5m);

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency { ValueAsXrp = 0 },
            Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"OneAssetWithdrawAll XRP result: {result}");
        AssertSuccess(res, "AMMWithdraw OneAssetWithdrawAll XRP");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        Assert.IsNotNull(infoAfter.Amm, "Pool should still exist — second holder has LP");
        Console.WriteLine($"Pool LP after withdrawal: {infoAfter.Amm.LPTokenBalance.Value}");
    }

    /// <summary>
    /// Two LP holders — first holder withdraws all LP into issued token via tfOneAssetWithdrawAll.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_OneAssetWithdrawAll_Token()
    {
        XrplWallet walletSecondHolder = await SetupSecondHolder();
        await CreatePool("1000", 10m);
        await DepositSecondHolder(walletSecondHolder, 0.5m);

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "0"
            },
            Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"OneAssetWithdrawAll Token result: {result}");
        AssertSuccess(res, "AMMWithdraw OneAssetWithdrawAll Token");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        Assert.IsNotNull(infoAfter.Amm, "Pool should still exist — second holder has LP");
        Console.WriteLine($"Pool LP after withdrawal: {infoAfter.Amm.LPTokenBalance.Value}");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_Simulate_ThenSubmit()
    {
        await CreatePool();

        AMMInfoResponse info = await GetAmmInfo();
        decimal withdrawLp = info.Amm.LPTokenBalance.ValueAsNumber * 0.5m;

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = withdrawLp
            },
            Flags = AMMWithdrawFlags.tfLPToken
        };

        ITransactionRequest autofilledForSim = await client.Autofill(withdraw);

        SimulateResponse simResult = await client.Simulate(new SimulateRequest
        {
            Transaction = autofilledForSim
        });

        Console.WriteLine($"Simulate result: {simResult.EngineResult}");
        Assert.IsTrue(
            simResult.EngineResult == "tesSUCCESS" || simResult.EngineResult == "terQUEUED",
            $"Simulate should succeed, got: {simResult.EngineResult}");

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMWithdraw after Simulate");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_LPToken_TypedModel()
    {
        await CreatePool();

        AMMInfoResponse info = await GetAmmInfo();
        decimal withdrawLp = info.Amm.LPTokenBalance.ValueAsNumber * 0.5m;

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = withdrawLp
            },
            Flags = AMMWithdrawFlags.tfLPToken
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        string json = withdraw.ToJson();
        Console.WriteLine($"Typed model JSON: {json}");

        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"Typed model submit result: {result}");

        Assert.AreEqual("tesSUCCESS", result,
            $"Typed model with LPTokenIn should succeed, got: {result}");
    }
}
