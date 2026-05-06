using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIAMMDeposit : TestIAMMBase
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

    [TestMethod]
    public async Task TestAMMDeposit_SingleAsset()
    {
        await CreatePool();

        AMMInfoResponse infoBefore = await GetAmmInfo();
        decimal lpBefore = infoBefore.Amm.LPTokenBalance.ValueAsNumber;

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "500"
            },
            Flags = AMMDepositFlags.tfSingleAsset
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMDeposit SingleAsset");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP before: {lpBefore}, after: {lpAfter}");

        Assert.IsTrue(lpAfter > lpBefore, "LP token supply should increase after deposit");
    }

    [TestMethod]
    public async Task TestAMMDeposit_TwoAssets()
    {
        await CreatePool();

        AMMInfoResponse infoBefore = await GetAmmInfo();
        decimal lpBefore = infoBefore.Amm.LPTokenBalance.ValueAsNumber;

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "200"
            },
            Amount2 = new Currency { ValueAsXrp = 2m },
            Flags = AMMDepositFlags.tfTwoAsset
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMDeposit TwoAssets");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP before: {lpBefore}, after: {lpAfter}");

        Assert.IsTrue(lpAfter > lpBefore, "LP token supply should increase after two-asset deposit");
    }

    [TestMethod]
    public async Task TestAMMDeposit_LPToken()
    {
        await CreatePool();

        AMMInfoResponse infoBefore = await GetAmmInfo();
        decimal lpBefore = infoBefore.Amm.LPTokenBalance.ValueAsNumber;
        decimal targetLp = lpBefore * 0.1m;

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenOut = new Currency
            {
                CurrencyCode = infoBefore.Amm.LPTokenBalance.CurrencyCode,
                Issuer = infoBefore.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = targetLp
            },
            Flags = AMMDepositFlags.tfLPToken
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMDeposit LPToken");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP before: {lpBefore}, after: {lpAfter}, requested: +{targetLp}");

        Assert.IsTrue(lpAfter > lpBefore, "LP token supply should increase after LP token deposit");
    }
}
