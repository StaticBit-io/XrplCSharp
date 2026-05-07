using System;
using System.Globalization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIAMMWithdraw : TestIAMMBase
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
    public async Task TestAMMWithdraw_LPToken()
    {
        await CreatePool();

        AMMInfoResponse info = await GetAmmInfo();
        decimal totalLp = info.Amm.LPTokenBalance.ValueAsNumber;
        decimal withdrawLp = totalLp * 0.5m;

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
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMWithdraw LPToken");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP total before: {totalLp}, after: {lpAfter}, withdrew: {withdrawLp}");

        Assert.IsTrue(lpAfter < totalLp, "LP token supply should decrease after withdrawal");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_WithdrawAll()
    {
        await CreatePool();

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMWithdraw WithdrawAll");
    }

    /// <summary>
    /// Regression test for G15 precision bug: uses exact string value from amm_info.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_FullLP_PrecisionTest()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse info = await GetAmmInfo();
        string exactLpValue = info.Amm.LPTokenBalance.Value;

        Console.WriteLine($"Exact LP value from amm_info: {exactLpValue}");
        Console.WriteLine($"LP currency: {info.Amm.LPTokenBalance.CurrencyCode}, issuer: {info.Amm.LPTokenBalance.Issuer}");

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                Value = exactLpValue
            },
            Flags = AMMWithdrawFlags.tfLPToken
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"Full LP withdraw result: {result}");

        Assert.AreEqual("tesSUCCESS", result,
            $"Full LP withdraw with exact server value should succeed, got: {result}");
    }

    /// <summary>
    /// Verifies decimal round-trip via ValueAsNumber does not exceed original LP value.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_FullLP_ViaValueAsNumber()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse info = await GetAmmInfo();
        string exactLpValue = info.Amm.LPTokenBalance.Value;
        decimal lpDecimal = info.Amm.LPTokenBalance.ValueAsNumber;

        Console.WriteLine($"LP from server: {exactLpValue}");
        Console.WriteLine($"LP as decimal: {lpDecimal}");

        Currency lpToken = new Currency
        {
            CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
            Issuer = info.Amm.LPTokenBalance.Issuer
        };
        lpToken.ValueAsNumber = lpDecimal;

        decimal roundTripped = decimal.Parse(lpToken.Value, CultureInfo.InvariantCulture);
        Console.WriteLine($"LP after round-trip: {lpToken.Value} (decimal: {roundTripped})");

        Assert.IsTrue(roundTripped <= lpDecimal,
            $"Round-tripped value ({roundTripped}) must not exceed original ({lpDecimal})");

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = lpToken,
            Flags = AMMWithdrawFlags.tfLPToken
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"Full LP withdraw via ValueAsNumber result: {result}");

        Assert.AreEqual("tesSUCCESS", result,
            $"Full LP withdraw via ValueAsNumber should succeed, got: {result}");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_SingleAsset()
    {
        await CreatePool();

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "100"
            },
            Flags = AMMWithdrawFlags.tfSingleAsset
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMWithdraw SingleAsset");
    }
}
