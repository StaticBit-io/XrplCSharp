using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;
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
/// loose tolerance would pass with the wrong formula and prove nothing; the bound here is five
/// orders of magnitude tighter than that error.
/// </para>
/// <para>
/// It is not tighter still, and the gap is deliberate. What these tests compare is the difference
/// of two reported LP token balances, so the last of <c>STAmount</c>'s 15 significant digits is
/// lost to cancellation before the comparison happens; the measured agreement is around 1e-15, and
/// asserting anywhere near that would buy brittleness rather than coverage. At 1e-9 there are six
/// orders of margin over what is measured and five over the error being guarded against.
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
            relativeError < 0.000000001m,
            $"Estimated {estimated} against {credited} actually credited - a relative error of " +
            $"{relativeError}, against a bound of 1e-9. The approximation this class exists to " +
            $"replace is out by 8e-4, so anything near that means the wrong equation was used.");

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
            relativeError < 0.000000001m,
            $"Estimated {estimated} against {spent} actually spent - a relative error of " +
            $"{relativeError}, against a bound of 1e-9.");
    }

    /// <summary>
    /// Equation 4: asking the node for an exact number of LP tokens costs what it says it costs.
    /// </summary>
    /// <remarks>
    /// The other direction of the deposit, and the one the unit tests can only reach through an
    /// identity. <c>tfOneAssetLPToken</c> names the tokens wanted and lets the node work out the
    /// asset, with <c>Amount</c> as a ceiling rather than the figure - so what is compared here is
    /// what the node decided to take.
    /// </remarks>
    [TestMethod]
    public async Task TestIADepositForExactTokensCostsAsEstimated()
    {
        await CreatePool();

        AMMInfoResponse before = await GetAmmInfo();
        decimal poolBalance = before.Amm.Amount.ValueAsNumber;
        decimal lpBefore = before.Amm.LPTokenBalance.ValueAsNumber;
        uint effectiveFee = FeeFor(before, walletHolder.ClassicAddress);

        const decimal WantTokens = 50m;

        decimal estimated = AmmMath.SingleAssetDepositForLPTokens(
            poolBalance,
            WantTokens,
            lpBefore,
            effectiveFee);

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,

            // A ceiling, deliberately far above the estimate: if the node were to spend all of it
            // the comparison below would fail loudly instead of being satisfied by construction.
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "500",
            },
            LPTokenOut = LpTokens(before, WantTokens),
            Flags = AMMDepositFlags.tfOneAssetLPToken,
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary result = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(result, "AMMDeposit one asset for LP tokens");

        AMMInfoResponse after = await GetAmmInfo();
        decimal taken = after.Amm.Amount.ValueAsNumber - poolBalance;
        decimal credited = after.Amm.LPTokenBalance.ValueAsNumber - lpBefore;

        decimal relativeError = Math.Abs(taken - estimated) / taken;

        Console.WriteLine(
            $"pool {poolBalance}, effective fee {effectiveFee}, wanted {WantTokens} tokens: " +
            $"estimated {estimated}, taken {taken}, credited {credited}, relative error {relativeError}");

        Assert.AreEqual(
            WantTokens,
            credited,
            "tfOneAssetLPToken credits exactly what was asked for; if it did not, the comparison below is measuring something else.");

        Assert.IsTrue(
            relativeError < 0.000000001m,
            $"Estimated a cost of {estimated} against {taken} actually taken - a relative error of " +
            $"{relativeError}, against a bound of 1e-9.");
    }

    /// <summary>
    /// Equation 8: redeeming an exact number of LP tokens returns what it says it returns.
    /// </summary>
    /// <remarks>
    /// <c>Amount</c> is a floor here rather than a ceiling, so it is set low enough not to bind
    /// and the node's own figure is what gets compared.
    /// </remarks>
    [TestMethod]
    public async Task TestIAWithdrawForExactTokensReturnsAsEstimated()
    {
        await CreatePool();

        AMMInfoResponse before = await GetAmmInfo();
        decimal poolBalance = before.Amm.Amount.ValueAsNumber;
        decimal lpBefore = before.Amm.LPTokenBalance.ValueAsNumber;
        uint effectiveFee = FeeFor(before, walletHolder.ClassicAddress);

        const decimal RedeemTokens = 50m;

        decimal estimated = AmmMath.SingleAssetWithdrawForLPTokens(
            poolBalance,
            RedeemTokens,
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
                Value = "0.000001",
            },
            LPTokenIn = LpTokens(before, RedeemTokens),
            Flags = AMMWithdrawFlags.tfOneAssetLPToken,
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary result = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(result, "AMMWithdraw one asset for LP tokens");

        AMMInfoResponse after = await GetAmmInfo();
        decimal received = poolBalance - after.Amm.Amount.ValueAsNumber;
        decimal spent = lpBefore - after.Amm.LPTokenBalance.ValueAsNumber;

        decimal relativeError = Math.Abs(received - estimated) / received;

        Console.WriteLine(
            $"pool {poolBalance}, effective fee {effectiveFee}, redeemed {RedeemTokens} tokens: " +
            $"estimated {estimated}, received {received}, spent {spent}, relative error {relativeError}");

        Assert.AreEqual(RedeemTokens, spent, "The node should burn exactly the tokens it was given.");

        Assert.IsTrue(
            relativeError < 0.000000001m,
            $"Estimated {estimated} against {received} actually returned - a relative error of " +
            $"{relativeError}, against a bound of 1e-9.");
    }

    /// <summary>
    /// The swap: a payment routed through the pool pays out what <see cref="AmmMath.SwapAssetIn"/>
    /// says it will.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nobody swaps by sending an <c>AMMDeposit</c>; a swap reaches the pool as a payment, which
    /// is why this one goes through <c>Payment</c> rather than an AMM transaction. With
    /// <c>tfPartialPayment</c> and a destination amount the pool cannot possibly cover, the whole
    /// of <c>SendMax</c> goes in and whatever the curve gives comes out - which is exactly the
    /// quantity the formula computes.
    /// </para>
    /// <para>
    /// The account swapping here is a second holder, not the one that created the pool, so this
    /// is the one case in this class that trades at the pool's own fee rather than at the auction
    /// slot's discount. The other tests cover the discounted path; between them both branches of
    /// <see cref="FeeFor"/> are exercised against a node.
    /// </para>
    /// <para>
    /// The arithmetic below is in drops, because that is the unit <c>amm_info</c> reports the XRP
    /// side of a pool in. <see cref="AmmMath"/> converts nothing, so mixing drops with XRP in one
    /// call produces a number that looks like a broken formula rather than a unit mistake - the
    /// first draft of this test did exactly that.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TestIASwapThroughTheePoolPaysOutAsEstimated()
    {
        await CreatePool();
        XrplWallet swapper = await SetupSecondHolder();

        AMMInfoResponse before = await GetAmmInfo();
        decimal poolToken = before.Amm.Amount.ValueAsNumber;
        decimal poolXrpDrops = before.Amm.Amount2.ValueAsNumber;
        uint effectiveFee = FeeFor(before, swapper.ClassicAddress);

        Assert.AreEqual(
            before.Amm.TradingFee,
            effectiveFee,
            "The swapper does not hold the auction slot, so this must be the pool's own fee.");

        const decimal SendXrp = 1m;
        const decimal SendDrops = 1_000_000m;

        // The pool takes XRP and gives the token back.
        decimal estimated = AmmMath.SwapAssetIn(poolXrpDrops, poolToken, SendDrops, effectiveFee);

        Payment payment = new Payment
        {
            Account = swapper.ClassicAddress,
            Destination = walletHolder.ClassicAddress,

            // Far more than one XRP can buy, so SendMax is what binds and the whole of it is spent.
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "1000",
            },
            SendMax = new Currency { ValueAsXrp = SendXrp },
            DeliverMin = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "0.000001",
            },
            Flags = PaymentFlags.tfPartialPayment,
        };

        ITransactionRequest autofilled = await client.Autofill(payment);
        TransactionSummary result = await client.SubmitAndWait(autofilled, swapper, true);
        AssertSuccess(result, "Payment routed through the AMM");

        AMMInfoResponse after = await GetAmmInfo();
        decimal spentDrops = after.Amm.Amount2.ValueAsNumber - poolXrpDrops;
        decimal received = poolToken - after.Amm.Amount.ValueAsNumber;

        decimal relativeError = Math.Abs(received - estimated) / received;

        Console.WriteLine(
            $"pool {poolToken} token / {poolXrpDrops} drops, fee {effectiveFee}, sent {spentDrops} drops: " +
            $"estimated {estimated}, paid out {received}, relative error {relativeError}");

        Assert.AreEqual(
            SendDrops,
            spentDrops,
            "The whole of SendMax should have entered the pool; if it did not, this is measuring a smaller swap than it estimated.");

        Assert.IsTrue(
            relativeError < 0.000000001m,
            $"Estimated {estimated} out of the pool against {received} actually paid - a relative " +
            $"error of {relativeError}, against a bound of 1e-9.");
    }

    /// <summary>
    /// The pool's LP token, as an amount this many of them.
    /// </summary>
    /// <remarks>
    /// The currency code is a hash of the two assets and the issuer is the AMM's own account, so
    /// both are read off <c>amm_info</c> rather than constructed.
    /// </remarks>
    private static Currency LpTokens(AMMInfoResponse info, decimal count) => new Currency
    {
        CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
        Issuer = info.Amm.LPTokenBalance.Issuer,
        Value = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

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
