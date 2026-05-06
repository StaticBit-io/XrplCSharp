using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Methods;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIAMMCreate : TestIAMMBase
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
    public async Task TestAMMCreate_XrpTokenPool()
    {
        TransactionSummary res = await CreatePool("1000", 10m);
        AssertSuccess(res, "AMMCreate XRP/Token");
    }

    [TestMethod]
    public async Task TestAMMCreate_VerifyPoolInfo()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse info = await GetAmmInfo();

        Assert.IsNotNull(info.Amm, "AMM info should not be null");
        Assert.IsNotNull(info.Amm.LPTokenBalance, "LP token balance should not be null");
        Assert.IsTrue(info.Amm.LPTokenBalance.ValueAsNumber > 0, "LP token balance should be positive");
        Assert.AreEqual(500u, info.Amm.TradingFee, "Trading fee should match creation value");

        Console.WriteLine($"Pool Amount: {info.Amm.Amount?.Value} {info.Amm.Amount?.CurrencyCode}");
        Console.WriteLine($"Pool Amount2: {info.Amm.Amount2?.Value} {info.Amm.Amount2?.CurrencyCode}");
        Console.WriteLine($"LP Balance: {info.Amm.LPTokenBalance.Value}");
    }
}
