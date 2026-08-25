using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="AmmMath"/> credits what the node credits - issue #133.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests prove the formulas were transcribed correctly from rippled's
/// <c>AMMHelpers.cpp</c>. They cannot prove they are the right formulas: a faithful copy of the
/// wrong equation passes every one of them. Only a node settles that, so this deposits into a real
/// pool and compares the estimate with what was actually credited.
/// </para>
/// <para>
/// The comparison is tight on purpose. The approximation this class replaces is out by 0.08%, so a
/// loose tolerance would pass with the wrong formula and prove nothing; the bound here is a
/// hundred times tighter than that error.
/// </para>
/// <para>
/// These tests also demonstrate the trap the report names first, because the first version of them
/// fell into it: <c>AMMCreate</c> hands the auction slot to the pool's creator, so the account
/// depositing here trades at <c>DiscountedFee</c> - a tenth of the pool's fee - and the node
/// credits it accordingly. Estimating at the pool's fee was out by 0.23%, three times the error of
/// the approximation this class exists to replace, with correct formulas throughout. Each test
/// asserts both halves: the right fee matches, and the pool's fee visibly does not.
/// </para>
/// </remarks>
[TestClass]
public class TestIAmmMathAgainstTheNode : TestIAMMBase
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
    public async Task TestIASingleAssetDepositIsCreditedAsEstimated()
    {
        await CreatePool();

        // Read immediately before the calculation, not once at the start: an estimate run against a
        // stale pool drifts in a way that looks exactly like an error in the arithmetic. That is one
        // of the two things the report names as making a correct formula appear wrong.
        AMMInfoResponse before = await GetAmmInfo();
        decimal poolBalance = before.Amm.Amount.ValueAsNumber;
        decimal lpBefore = before.Amm.LPTokenBalance.ValueAsNumber;
        uint poolFee = before.Amm.TradingFee;
        uint effectiveFee = FeeFor(before, walletHolder.ClassicAddress);

        const decimal Deposit = 500m;

        decimal estimated = AmmMath.LPTokensForSingleAssetDeposit(
            poolBalance,
            Deposit,
            lpBefore,
            effectiveFee);
        decimal atPoolFee = AmmMath.LPTokensForSingleAssetDeposit(
            poolBalance,
            Deposit,
            lpBefore,
            poolFee);

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = Deposit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Flags = AMMDepositFlags.tfSingleAsset,
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary result = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(result, "AMMDeposit single asset");

        AMMInfoResponse after = await GetAmmInfo();
        decimal credited = after.Amm.LPTokenBalance.ValueAsNumber - lpBefore;

        decimal relativeError = Math.Abs(credited - estimated) / credited;

        Console.WriteLine(
            $"pool {poolBalance}, pool fee {poolFee}, effective fee {effectiveFee}, deposit {Deposit}: " +
            $"estimated {estimated}, credited {credited}, relative error {relativeError}");

        Assert.IsTrue(
            relativeError < 0.00001m,
            $"Estimated {estimated} against {credited} actually credited - a relative error of " +
            $"{relativeError}. The approximation this class exists to replace is out by 0.0008, so " +
            $"anything near that means the wrong equation was used.");

        Assert.AreNotEqual(
            poolFee,
            effectiveFee,
            "This account holds the auction slot, which is what makes the second assertion mean something.");
        Assert.IsTrue(
            Math.Abs(atPoolFee - credited) / credited > 0.001m,
            $"Estimating at the pool's fee instead of the slot holder's must be visibly wrong, and is: " +
            $"{atPoolFee} against {credited}.");
    }

    /// <summary>
    /// And the withdrawal side, which the unit tests can only pin through an identity.
    /// </summary>
    [TestMethod]
    public async Task TestIASingleAssetWithdrawCostsAsEstimated()
    {
        await CreatePool();

        AMMInfoResponse before = await GetAmmInfo();
        decimal poolBalance = before.Amm.Amount.ValueAsNumber;
        decimal lpBefore = before.Amm.LPTokenBalance.ValueAsNumber;
        uint poolFee = before.Amm.TradingFee;
        uint effectiveFee = FeeFor(before, walletHolder.ClassicAddress);

        const decimal Withdraw = 100m;

        decimal estimated = AmmMath.LPTokensForSingleAssetWithdraw(
            poolBalance,
            Withdraw,
            lpBefore,
            effectiveFee);

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = Withdraw.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Flags = AMMWithdrawFlags.tfSingleAsset,
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary result = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(result, "AMMWithdraw single asset");

        AMMInfoResponse after = await GetAmmInfo();
        decimal spent = lpBefore - after.Amm.LPTokenBalance.ValueAsNumber;

        decimal relativeError = Math.Abs(spent - estimated) / spent;

        Console.WriteLine(
            $"pool {poolBalance}, pool fee {poolFee}, effective fee {effectiveFee}, withdraw {Withdraw}: " +
            $"estimated {estimated}, spent {spent}, relative error {relativeError}");

        Assert.IsTrue(
            relativeError < 0.00001m,
            $"Estimated {estimated} against {spent} actually spent - a relative error of {relativeError}.");
    }

    /// <summary>
    /// The fee this account actually trades at, which is not the pool's fee if it holds the slot.
    /// </summary>
    /// <remarks>
    /// What a consumer has to do too, and the reason <see cref="AmmMath.DiscountedTradingFee"/>
    /// exists. The node's own <c>discounted_fee</c> is used rather than computed, and checked
    /// against what the SDK would have computed - if those ever disagree, the SDK's constant is
    /// wrong and this says so.
    /// </remarks>
    private static uint FeeFor(AMMInfoResponse info, string account)
    {
        AuctionSlot slot = info.Amm.AuctionSlot;
        if (slot is null || !string.Equals(slot.Account, account, StringComparison.Ordinal))
        {
            return info.Amm.TradingFee;
        }

        Assert.AreEqual(
            slot.DiscountedFee,
            AmmMath.DiscountedTradingFee(info.Amm.TradingFee),
            "The SDK's idea of the auction slot discount must be the node's.");

        return slot.DiscountedFee;
    }
}
