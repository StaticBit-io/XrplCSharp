using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// AMM over MPT assets (XLS-62, featureMPTokensV2): every AMM transaction type with an
/// MPT in its Asset/Amount fields. The MPT issue goes through a different codec branch than
/// an IOU issue (STIssue with an MPTID instead of currency+issuer), so each transaction type
/// is exercised on its own rather than assumed from AMMCreate.
/// XLS-62: https://github.com/XRPLF/XRPL-Standards/discussions/231
/// </summary>
[TestClass]
[TestCategory("AMM")]
public class TestIAMMMpt
{
    public TestContext TestContext { get; set; }

    private static IXrplClient client;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;
    private static bool mptokensV2Usable;

    private const string PoolMptAmount = "1000";
    private const decimal PoolXrpAmount = 10m;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
        // On the standalone stands MPTokensV2 is a [features] Rules preset (Supported::No on the
        // release build), invisible to the on-ledger guard: run there unconditionally and let a
        // missing preset fail loudly with temDISABLED. On a public network trust the guard.
        mptokensV2Usable = IntegrationTestConfig.IsStandalone()
            || await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.MPTokensV2);
    }

    [TestInitialize]
    public void CheckMPTokensV2Amendment()
    {
        if (!mptokensV2Usable)
        {
            Assert.Inconclusive("MPTokensV2 amendment (XLS-62) is not enabled on the test node.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private sealed record MptPool(XrplWallet Issuer, XrplWallet Holder, string IssuanceId);

    private static IssuedCurrency MptAsset(string issuanceId) => new IssuedCurrency { MptIssuanceId = issuanceId };

    private static IssuedCurrency XrpAsset => new IssuedCurrency { Currency = "XRP" };

    private static Currency Mpt(string issuanceId, string value) => new Currency { Value = value, MPTokenIssuanceID = issuanceId };

    private static void AssertSuccess(TransactionSummary res, string context)
    {
        string result = res.Meta?.TransactionResult;
        Assert.IsTrue(
            result is "tesSUCCESS" or "terQUEUED",
            $"{context} failed: {result}");
    }

    private static async Task<TransactionSummary> SubmitAsync(ITransactionRequest tx, XrplWallet signer, string context)
    {
        ITransactionRequest autofilled = await client.Autofill(tx);
        TransactionSummary res = await client.SubmitAndWait(autofilled, signer, true);
        AssertSuccess(res, context);
        return res;
    }

    /// <summary>
    /// Issues an MPT that can be traded on the AMM and hands <paramref name="holder"/> 10 000 units.
    /// </summary>
    private static async Task<string> IssueMptAsync(XrplWallet issuer, XrplWallet holder, MPTokenIssuanceCreateFlags extraFlags = 0)
    {
        MPTokenIssuanceCreate create = new MPTokenIssuanceCreate
        {
            Account = issuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTrade | MPTokenIssuanceCreateFlags.tfMPTCanTransfer | extraFlags,
        };
        TransactionSummary createRes = await SubmitAsync(create, issuer, "MPTokenIssuanceCreate");
        string issuanceId = createRes.Meta?.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be present in metadata");

        await SubmitAsync(new MPTokenAuthorize
        {
            Account = holder.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        }, holder, "MPTokenAuthorize");

        await SubmitAsync(new Payment
        {
            Account = issuer.ClassicAddress,
            Destination = holder.ClassicAddress,
            Amount = Mpt(issuanceId, "10000"),
        }, issuer, "MPT Payment");

        return issuanceId;
    }

    private static async Task<MptPool> CreateMptXrpPoolAsync(MPTokenIssuanceCreateFlags extraFlags = 0)
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);

        string issuanceId = await IssueMptAsync(issuer, holder, extraFlags);

        await SubmitAsync(new AMMCreate
        {
            Account = holder.ClassicAddress,
            Amount = Mpt(issuanceId, PoolMptAmount),
            Amount2 = new Currency { ValueAsXrp = PoolXrpAmount },
            TradingFee = 500,
        }, holder, "AMMCreate MPT/XRP pool");

        return new MptPool(issuer, holder, issuanceId);
    }

    private static Task<AMMInfoResponse> GetPoolInfoAsync(string issuanceId) =>
        client.AmmInfo(new AMMInfoRequest { Asset = MptAsset(issuanceId), Asset2 = XrpAsset }).Typed();

    [TestMethod]
    public async Task TestAMMCreate_MptXrpPool_Succeeds()
    {
        MptPool pool = await CreateMptXrpPoolAsync();

        AMMInfoResponse info = await GetPoolInfoAsync(pool.IssuanceId);
        Assert.IsTrue(info.Amm.LPTokenBalance.ValueAsNumber > 0, "the pool must have issued LP tokens");

        // The AMM pseudo-account holds the pool's MPT through an MPToken entry flagged lsfMPTAMM
        AccountObjects objects = await client.AccountObjects(new AccountObjectsRequest(info.Amm.Account)
        {
            Type = LedgerEntryType.MPToken,
        }).Typed();
        LOMPToken poolToken = objects.AccountObjectList?.OfType<LOMPToken>()
            .FirstOrDefault(t => string.Equals(t.MPTokenIssuanceID, pool.IssuanceId, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(poolToken, "the AMM account must hold an MPToken for the pool asset");
        Assert.IsTrue(poolToken.Flags is { } flags && flags.HasFlag(MPTokenFlags.lsfMPTAMM),
            $"the AMM account's MPToken must carry lsfMPTAMM, got {poolToken.Flags}");
    }

    [TestMethod]
    public async Task TestAMMCreate_MptMptPool_Succeeds()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);

        string first = await IssueMptAsync(issuer, holder);
        string second = await IssueMptAsync(issuer, holder);

        await SubmitAsync(new AMMCreate
        {
            Account = holder.ClassicAddress,
            Amount = Mpt(first, PoolMptAmount),
            Amount2 = Mpt(second, PoolMptAmount),
            TradingFee = 500,
        }, holder, "AMMCreate MPT/MPT pool");

        AMMInfoResponse info = await client.AmmInfo(new AMMInfoRequest { Asset = MptAsset(first), Asset2 = MptAsset(second) }).Typed();
        Assert.IsTrue(info.Amm.LPTokenBalance.ValueAsNumber > 0, "the MPT/MPT pool must have issued LP tokens");
    }

    [TestMethod]
    public async Task TestAMMDeposit_Mpt_SingleAsset()
    {
        MptPool pool = await CreateMptXrpPoolAsync();
        decimal lpBefore = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;

        await SubmitAsync(new AMMDeposit
        {
            Account = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
            Amount = Mpt(pool.IssuanceId, "100"),
            Flags = AMMDepositFlags.tfSingleAsset,
        }, pool.Holder, "AMMDeposit tfSingleAsset MPT");

        decimal lpAfter = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;
        Assert.IsTrue(lpAfter > lpBefore, $"LP supply must grow after a single-asset MPT deposit ({lpBefore} -> {lpAfter})");
    }

    [TestMethod]
    public async Task TestAMMDeposit_Mpt_TwoAsset()
    {
        MptPool pool = await CreateMptXrpPoolAsync();
        decimal lpBefore = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;

        await SubmitAsync(new AMMDeposit
        {
            Account = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
            Amount = Mpt(pool.IssuanceId, "100"),
            Amount2 = new Currency { ValueAsXrp = 1m },
            Flags = AMMDepositFlags.tfTwoAsset,
        }, pool.Holder, "AMMDeposit tfTwoAsset MPT+XRP");

        decimal lpAfter = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;
        Assert.IsTrue(lpAfter > lpBefore, $"LP supply must grow after a two-asset deposit ({lpBefore} -> {lpAfter})");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_Mpt_SingleAsset()
    {
        MptPool pool = await CreateMptXrpPoolAsync();
        decimal lpBefore = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;

        await SubmitAsync(new AMMWithdraw
        {
            Account = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
            Amount = Mpt(pool.IssuanceId, "100"),
            Flags = AMMWithdrawFlags.tfSingleAsset,
        }, pool.Holder, "AMMWithdraw tfSingleAsset MPT");

        decimal lpAfter = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;
        Assert.IsTrue(lpAfter < lpBefore, $"LP supply must shrink after a single-asset MPT withdrawal ({lpBefore} -> {lpAfter})");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_Mpt_WithdrawAll_RemovesPool()
    {
        MptPool pool = await CreateMptXrpPoolAsync();

        await SubmitAsync(new AMMWithdraw
        {
            Account = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll,
        }, pool.Holder, "AMMWithdraw tfWithdrawAll MPT");

        // The sole LP holder withdrawing everything deletes the pool
        try
        {
            AMMInfoResponse info = await GetPoolInfoAsync(pool.IssuanceId);
            Assert.Fail($"the pool must be gone after the sole holder withdraws everything, LP balance: {info.Amm?.LPTokenBalance?.Value}");
        }
        catch (RippledException ex) when (ex.Message.Contains("actNotFound") || ex.Message.Contains("ammNotFound"))
        {
            Console.WriteLine($"Pool removed after WithdrawAll: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestAMMVote_Mpt()
    {
        MptPool pool = await CreateMptXrpPoolAsync();

        await SubmitAsync(new AMMVote
        {
            Account = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
            TradingFee = 100,
        }, pool.Holder, "AMMVote MPT pool");

        AMMInfoResponse info = await GetPoolInfoAsync(pool.IssuanceId);
        Assert.AreEqual(100u, info.Amm.TradingFee, "the sole LP holder's vote sets the trading fee");
    }

    [TestMethod]
    public async Task TestAMMBid_Mpt()
    {
        MptPool pool = await CreateMptXrpPoolAsync();

        await SubmitAsync(new AMMBid
        {
            Account = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
        }, pool.Holder, "AMMBid MPT pool");

        AMMInfoResponse info = await GetPoolInfoAsync(pool.IssuanceId);
        Assert.IsNotNull(info.Amm.AuctionSlot, "the auction slot must be taken after the bid");
        Assert.AreEqual(pool.Holder.ClassicAddress, info.Amm.AuctionSlot.Account, "the bidder must own the auction slot");
    }

    [TestMethod]
    public async Task TestAMMClawback_Mpt()
    {
        MptPool pool = await CreateMptXrpPoolAsync(MPTokenIssuanceCreateFlags.tfMPTCanClawback);
        decimal lpBefore = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;

        await SubmitAsync(new AMMClawBack
        {
            Account = pool.Issuer.ClassicAddress,
            Holder = pool.Holder.ClassicAddress,
            Asset = MptAsset(pool.IssuanceId),
            Asset2 = XrpAsset,
            Amount = Mpt(pool.IssuanceId, "100"),
        }, pool.Issuer, "AMMClawback MPT");

        decimal lpAfter = (await GetPoolInfoAsync(pool.IssuanceId)).Amm.LPTokenBalance.ValueAsNumber;
        Assert.IsTrue(lpAfter < lpBefore, $"clawing back pool MPT burns the holder's LP tokens ({lpBefore} -> {lpAfter})");
    }
}
