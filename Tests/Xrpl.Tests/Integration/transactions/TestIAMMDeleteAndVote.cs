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
public class TestIAMMDeleteAndVote : TestIAMMBase
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

    #region AMMDelete Tests

    [TestMethod]
    public async Task TestAMMDelete_EmptyPool()
    {
        await CreatePool();

        AMMWithdraw withdrawAll = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };

        ITransactionRequest autofilledWithdraw = await client.Autofill(withdrawAll);
        TransactionSummary withdrawRes = await client.SubmitAndWait(autofilledWithdraw, walletHolder, true);
        Console.WriteLine($"Withdraw all: {withdrawRes.Meta?.TransactionResult}");
        AssertSuccess(withdrawRes, "WithdrawAll");

        try
        {
            AMMInfoResponse info = await GetAmmInfo();
            Console.WriteLine($"Pool still exists after WithdrawAll, LP balance: {info.Amm?.LPTokenBalance?.Value}");
            Assert.Fail("Pool should have been auto-deleted after sole LP holder WithdrawAll");
        }
        catch (RippledException ex) when (
            ex.Message.Contains("actNotFound") ||
            ex.Message.Contains("terNO_AMM") ||
            ex.Message.Contains("ammNotFound"))
        {
            Console.WriteLine($"Pool correctly auto-deleted after sole LP holder WithdrawAll: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestAMMDelete_NonEmptyPool_Fails()
    {
        await CreatePool();

        AMMDelete delete = new AMMDelete
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset
        };

        try
        {
            ITransactionRequest autofilled = await client.Autofill(delete);
            TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
            string result = res.Meta?.TransactionResult;
            Console.WriteLine($"AMMDelete non-empty pool result: {result}");

            Assert.AreNotEqual("tesSUCCESS", result,
                "AMMDelete on non-empty pool should not succeed");
        }
        catch (RippleException ex)
        {
            Console.WriteLine($"AMMDelete non-empty pool correctly rejected: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestAMMDelete_AfterPartialWithdraw()
    {
        XrplWallet walletSecondHolder = await SetupSecondHolder();
        await CreatePool("1000", 10m);
        await DepositSecondHolder(walletSecondHolder, 0.2m);

        AMMWithdraw withdrawAll = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };
        ITransactionRequest autoWithdraw = await client.Autofill(withdrawAll);
        TransactionSummary withdrawRes = await client.SubmitAndWait(autoWithdraw, walletHolder, true);
        AssertSuccess(withdrawRes, "First holder WithdrawAll");

        AMMWithdraw withdrawAll2 = new AMMWithdraw
        {
            Account = walletSecondHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };
        ITransactionRequest autoWithdraw2 = await client.Autofill(withdrawAll2);
        TransactionSummary withdrawRes2 = await client.SubmitAndWait(autoWithdraw2, walletSecondHolder, true);
        AssertSuccess(withdrawRes2, "Second holder WithdrawAll");

        try
        {
            AMMInfoResponse info2 = await GetAmmInfo();
            Console.WriteLine($"Pool still exists after full withdrawal, LP balance: {info2.Amm?.LPTokenBalance?.Value}");

            AMMDelete delete = new AMMDelete
            {
                Account = walletHolder.ClassicAddress,
                Asset = TokenAsset,
                Asset2 = XrpAsset
            };

            ITransactionRequest autoDelete = await client.Autofill(delete);
            TransactionSummary deleteRes = await client.SubmitAndWait(autoDelete, walletHolder, true);
            string result = deleteRes.Meta?.TransactionResult;
            Console.WriteLine($"AMMDelete result: {result}");
            Assert.IsTrue(
                result == "tesSUCCESS" || result == "terQUEUED" || result == "tecAMM_EMPTY",
                $"AMMDelete after full withdrawal failed: {result}");
        }
        catch (RippledException ex) when (
            ex.Message.Contains("terNO_AMM") ||
            ex.Message.Contains("ammNotFound") ||
            ex.Message.Contains("actNotFound"))
        {
            Console.WriteLine($"Pool was auto-deleted when last LP holder withdrew: {ex.Message}");
        }
    }

    #endregion

    #region AMMVote Tests

    [TestMethod]
    public async Task TestAMMVote_ChangeTradingFee()
    {
        await CreatePool();

        AMMVote vote = new AMMVote
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            TradingFee = 100
        };

        ITransactionRequest autofilled = await client.Autofill(vote);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMVote");

        AMMInfoResponse info = await GetAmmInfo();
        Console.WriteLine($"Trading fee after vote: {info.Amm.TradingFee}");
        Assert.AreEqual(100u, info.Amm.TradingFee, "Trading fee should reflect the vote");
    }

    #endregion
}
