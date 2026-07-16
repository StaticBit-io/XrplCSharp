// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/gatewayBalances.ts

using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIGatewayBalances
{
    public TestContext TestContext { get; set; }

    static IXrplClient client;
    private static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client?.Dispose();
    }

    private static async Task SubmitAsync(TransactionRequest tx, XrplWallet wallet)
    {
        var res = await client.SubmitAndWait(tx, wallet, true);
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
    }

    private static Task TrustAsync(XrplWallet holder, XrplWallet issuer, string currencyCode, string limit)
        => SubmitAsync(new TrustSet
        {
            Account = holder.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = issuer.ClassicAddress,
                Value = limit,
            },
        }, holder);

    private static Task PayIouAsync(XrplWallet from, XrplWallet to, XrplWallet issuer, string currencyCode, string value)
        => SubmitAsync(new Payment
        {
            Account = from.ClassicAddress,
            Destination = to.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = issuer.ClassicAddress,
                Value = value,
            },
        }, from);

    /// <summary>
    /// Builds a gateway on the standalone node and pins every gateway_balances
    /// section against rippled's bucketing (GatewayBalances.cpp): hot-wallet
    /// lines go to `balances`, positive issuer balances to `assets`, frozen
    /// lines to `frozen_balances` (excluded from `obligations`), the rest sums
    /// into `obligations`.
    /// </summary>
    [TestMethod]
    public async Task TestRequestMethod()
    {
        const string USD = "USD";
        const string FOO = "FOO";

        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder1 = XrplWallet.Generate();
        XrplWallet holder2 = XrplWallet.Generate();
        XrplWallet hotWallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder1, holder2, hotWallet);

        // obligations + balances: holders and the hot wallet trust the gateway and receive IOU
        await TrustAsync(holder1, issuer, USD, "10000");
        await TrustAsync(holder2, issuer, USD, "10000");
        await TrustAsync(hotWallet, issuer, USD, "10000");
        await PayIouAsync(issuer, holder1, issuer, USD, "100");
        await PayIouAsync(issuer, holder2, issuer, USD, "50");
        await PayIouAsync(issuer, hotWallet, issuer, USD, "25");

        // frozen_balances: the gateway freezes its line to holder2
        await SubmitAsync(new TrustSet
        {
            Account = issuer.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = USD,
                Issuer = holder2.ClassicAddress,
                Value = "0",
            },
            Flags = TrustSetFlags.tfSetFreeze,
        }, issuer);

        // assets: the gateway itself holds an IOU issued by holder1
        await TrustAsync(issuer, holder1, FOO, "1000");
        await PayIouAsync(holder1, issuer, holder1, FOO, "7");

        var request = new GatewayBalancesRequest(issuer.ClassicAddress)
        {
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
            Strict = true,
            HotWallet = hotWallet.ClassicAddress,
        };
        GatewayBalancesResponse response = await client.GatewayBalances(request);

        Assert.IsNotNull(response);
        Assert.AreEqual(issuer.ClassicAddress, response.Account);

        // obligations: only holder1's line — the hot wallet is excluded by the
        // hotwallet param and holder2's frozen line lands in frozen_balances
        var obligation = response.Obligations.FirstOrDefault(c => c.CurrencyCode == USD);
        Assert.IsNotNull(obligation, "obligations must contain the USD line");
        Assert.AreEqual("100", obligation.Value);

        var hotBalance = response.Balances.FirstOrDefault(c =>
            c.Issuer == hotWallet.ClassicAddress && c.CurrencyCode == USD);
        Assert.IsNotNull(hotBalance, "balances must contain the hot wallet line");
        Assert.AreEqual("25", hotBalance.Value);

        var frozen = response.FrozenBalances.FirstOrDefault(c =>
            c.Issuer == holder2.ClassicAddress && c.CurrencyCode == USD);
        Assert.IsNotNull(frozen, "frozen_balances must contain holder2's line");
        Assert.AreEqual("50", frozen.Value);

        var asset = response.Assets.FirstOrDefault(c =>
            c.Issuer == holder1.ClassicAddress && c.CurrencyCode == FOO);
        Assert.IsNotNull(asset, "assets must contain the FOO holding");
        Assert.AreEqual("7", asset.Value);
    }
}
