using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="AmmLiquidity"/> against the node: every deposit and withdrawal is quoted from a
/// fresh <c>amm_info</c>, previewed through <c>simulate</c>, compared, and submitted when the
/// node accepts it, so each step starts from the pool the previous one left.
/// </summary>
[TestClass]
[TestCategory("AMM")]
public class TestIAmmLiquidity : TestIAMMBase
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

    private delegate AmmQuote Quote(AmmPool pool, XrplNumber holderTokens, bool onlyProvider, LedgerRules rules);

    private delegate ITransactionCommon Build(AmmPool pool, string account, Currency lpToken);

    [TestMethod]
    public async Task TestAmmLiquidity_TokenXrp_MatchesWhatTheNodeMoves()
    {
        AssertSuccess(await CreatePool("1234.5678", 98.765432m), "AMMCreate");
        XrplWallet other = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, other);
        await TrustAndFund(other, "5000");

        IssuedCurrency token = TokenAsset;
        IssuedCurrency xrp = XrpAsset;
        Currency Tok(string v) => new Currency { CurrencyCode = token.Currency, Issuer = token.Issuer, Value = v };
        Currency Drops(string v) => new Currency { CurrencyCode = "XRP", Value = v };
        Currency Lp(Currency lp, string v) => new Currency { CurrencyCode = lp.CurrencyCode, Issuer = lp.Issuer, Value = v };

        (string Name, XrplWallet Account, bool IsDeposit, Quote Quote, Build Build)[] steps =
        {
            ("single token deposit", walletHolder, true,
                (p, h, o, r) => AmmLiquidity.DepositSingle(p, token, XrplNumber.Parse("12.3456789"), rules: r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("12.3456789"), Flags = AMMDepositFlags.tfSingleAsset }),
            ("single XRP deposit", other, true,
                (p, h, o, r) => AmmLiquidity.DepositSingle(p, xrp, XrplNumber.Parse("3333333"), rules: r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Drops("3333333"), Flags = AMMDepositFlags.tfSingleAsset }),
            ("deposit for LP tokens", other, true,
                (p, h, o, r) => AmmLiquidity.DepositForTokens(p, XrplNumber.Parse("777.123"), rules: r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, LPTokenOut = Lp(lp, "777.123"), Flags = AMMDepositFlags.tfLPToken }),
            ("two-asset deposit", other, true,
                (p, h, o, r) => AmmLiquidity.DepositBoth(p, XrplNumber.Parse("50"), XrplNumber.Parse("9999999"), rules: r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("50"), Amount2 = Drops("9999999"), Flags = AMMDepositFlags.tfTwoAsset }),
            ("single token for LP tokens", other, true,
                (p, h, o, r) => AmmLiquidity.DepositSingleForTokens(p, token, XrplNumber.Parse("100"), XrplNumber.Parse("123.456"), r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("100"), LPTokenOut = Lp(lp, "123.456"), Flags = AMMDepositFlags.tfOneAssetLPToken }),
            ("single token for LP tokens, most allowed too small", other, true,
                (p, h, o, r) => AmmLiquidity.DepositSingleForTokens(p, token, XrplNumber.Parse("0.0001"), XrplNumber.Parse("123.456"), r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("0.0001"), LPTokenOut = Lp(lp, "123.456"), Flags = AMMDepositFlags.tfOneAssetLPToken }),
            ("deposit within a generous effective price", other, true,
                (p, h, o, r) => AmmLiquidity.DepositWithEffectivePrice(p, token, XrplNumber.Parse("10"), XrplNumber.Parse("1"), r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("10"), EPrice = Tok("1"), Flags = AMMDepositFlags.tfLimitLPToken }),
            ("deposit capped by a tight effective price", other, true,
                (p, h, o, r) => AmmLiquidity.DepositWithEffectivePrice(p, token, XrplNumber.Parse("500"), XrplNumber.Parse("0.00715"), r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("500"), EPrice = Tok("0.00715"), Flags = AMMDepositFlags.tfLimitLPToken }),
            ("deposit up to an effective price alone", other, true,
                (p, h, o, r) => AmmLiquidity.DepositWithEffectivePrice(p, token, XrplNumber.Zero, XrplNumber.Parse("0.0072"), r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("0"), EPrice = Tok("0.0072"), Flags = AMMDepositFlags.tfLimitLPToken }),
            ("tiny single deposit", other, true,
                (p, h, o, r) => AmmLiquidity.DepositSingle(p, xrp, XrplNumber.Parse("1"), rules: r),
                (p, a, lp) => new AMMDeposit { Account = a, Asset = token, Asset2 = xrp, Amount = Drops("1"), Flags = AMMDepositFlags.tfSingleAsset }),
            ("withdraw for LP tokens", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawForTokens(p, XrplNumber.Parse("333.3333"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, LPTokenIn = Lp(lp, "333.3333"), Flags = AMMWithdrawFlags.tfLPToken }),
            ("two-asset withdrawal", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawBoth(p, XrplNumber.Parse("10"), XrplNumber.Parse("555555"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("10"), Amount2 = Drops("555555"), Flags = AMMWithdrawFlags.tfTwoAsset }),
            ("single token withdrawal", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawSingle(p, token, XrplNumber.Parse("7.654321"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("7.654321"), Flags = AMMWithdrawFlags.tfSingleAsset }),
            ("single XRP for LP tokens", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawSingleForTokens(p, xrp, XrplNumber.Parse("22.22"), XrplNumber.Zero, h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = xrp, Asset2 = token, Amount = Drops("0"), LPTokenIn = Lp(lp, "22.22"), Flags = AMMWithdrawFlags.tfOneAssetLPToken }),
            ("single XRP for LP tokens, least accepted too large", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawSingleForTokens(p, xrp, XrplNumber.Parse("22.22"), XrplNumber.Parse("1000000"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = xrp, Asset2 = token, Amount = Drops("1000000"), LPTokenIn = Lp(lp, "22.22"), Flags = AMMWithdrawFlags.tfOneAssetLPToken }),
            ("withdraw at an effective price", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawWithEffectivePrice(p, token, XrplNumber.Zero, XrplNumber.Parse("138.8"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, Amount = Tok("0"), EPrice = Lp(lp, "138.8"), Flags = AMMWithdrawFlags.tfLimitLPToken }),
            ("withdraw more than held", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawForTokens(p, XrplNumber.Parse("99999999"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, LPTokenIn = Lp(lp, "99999999"), Flags = AMMWithdrawFlags.tfLPToken }),
            ("other withdraws all as XRP", other, false,
                (p, h, o, r) => AmmLiquidity.WithdrawAllOfAsset(p, xrp, XrplNumber.Zero, h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = xrp, Asset2 = token, Amount = Drops("0"), Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll }),
            ("holder, the only provider, withdraws for LP tokens", walletHolder, false,
                (p, h, o, r) => AmmLiquidity.WithdrawForTokens(p, XrplNumber.Parse("100"), h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, LPTokenIn = Lp(lp, "100"), Flags = AMMWithdrawFlags.tfLPToken }),
            ("holder withdraws all", walletHolder, false,
                (p, h, o, r) => AmmLiquidity.WithdrawAll(p, h, o, r),
                (p, a, lp) => new AMMWithdraw { Account = a, Asset = token, Asset2 = xrp, Flags = AMMWithdrawFlags.tfWithdrawAll }),
        };

        await Exercise(token, xrp, steps, new[] { walletHolder, other });
    }

    [TestMethod]
    public async Task TestInitialLpTokens_MatchWhatAMMCreateIssues()
    {
        // tfTwoAssetIfEmpty issues the tokens AMMCreate does: ammLPTokens, sqrt(amount * amount2)
        // rounded down. An odd pair keeps the root irrational.
        AssertSuccess(await CreatePool("1234.5678", 98.765432m), "AMMCreate");
        AMMInfo amm = (await client.AmmInfo(new AMMInfoRequest { Asset = TokenAsset, Asset2 = XrpAsset }).Typed()).Amm;
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);

        decimal offline = AmmLiquidity.InitialLpTokens(XrplNumber.Parse("1234.5678"), XrplNumber.Parse("98765432"), rules);
        Assert.AreEqual(XrplNumber.Parse(amm.LPTokenBalance.Value), (XrplNumber)offline);

        AmmPool empty = new AmmPool
        {
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Balance = XrplNumber.Zero,
            Balance2 = XrplNumber.Zero,
            LpTokenBalance = XrplNumber.Zero,
        };
        AmmQuote refill = AmmLiquidity.DepositIntoEmptyPool(empty, XrplNumber.Parse("1234.5678"), XrplNumber.Parse("98765432"), rules);
        Assert.IsTrue(refill.IsAccepted, refill.RefusalReason);
        Assert.AreEqual(offline, refill.LpTokens);
        Assert.AreEqual("tecAMM_NOT_EMPTY", AmmLiquidity.DepositIntoEmptyPool(AmmPool.FromAmmInfo(amm), 1, 1, rules).Refusal);
    }

    private async Task Exercise(
        IssuedCurrency asset,
        IssuedCurrency asset2,
        (string Name, XrplWallet Account, bool IsDeposit, Quote Quote, Build Build)[] steps,
        XrplWallet[] providers)
    {
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        int accepted = 0;
        foreach ((string name, XrplWallet account, bool isDeposit, Quote quoteOf, Build build) in steps)
        {
            AMMInfo amm = (await client.AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2 }).Typed()).Amm;
            AmmPool pool = AmmPool.FromAmmInfo(amm, account.ClassicAddress, await IntegrationTestConfig.ValidatedCloseTimeAsync(client));
            Dictionary<string, XrplNumber> holdings = new Dictionary<string, XrplNumber>();
            foreach (XrplWallet provider in providers)
                holdings[provider.ClassicAddress] = await LpTokensOf(provider, asset, asset2);

            XrplNumber held = holdings[account.ClassicAddress];
            int holders = 0;
            foreach (XrplNumber h in holdings.Values)
            {
                if (!h.IsZero)
                    holders++;
            }

            bool onlyProvider = holders == 1 && !held.IsZero;
            AmmQuote quote = quoteOf(pool, held, onlyProvider, rules);
            ITransactionCommon tx = build(pool, account.ClassicAddress, amm.LPTokenBalance);
            TransactionPreview<AmmOutcome> preview = isDeposit
                ? await client.PreviewAMMDeposit((AMMDeposit)tx)
                : await client.PreviewAMMWithdraw((AMMWithdraw)tx);

            if (!preview.WouldSucceed)
            {
                Assert.AreEqual(preview.EngineResult, quote.Refusal, $"{name}: {quote.RefusalReason}");
                continue;
            }

            Assert.IsTrue(quote.IsAccepted, $"{name}: offline {quote.Refusal} ({quote.RefusalReason})");
            AssertSuccess(await client.SubmitAndWait(preview.Transaction, account, false), name);
            accepted++;

            // The pool as the ledger now holds it: every balance to its last digit.
            if (quote.PoolAfter.LpTokenBalance.IsZero)
            {
                bool exists = true;
                try
                {
                    AMMInfoResponse remaining = await client.AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2 }).Typed();
                    exists = remaining?.Amm != null;
                }
                catch (Exception)
                {
                    exists = false;
                }

                Assert.IsFalse(exists, $"{name}: the pool should be gone");
                continue;
            }

            AmmPool after = AmmPool.FromAmmInfo((await client.AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2 }).Typed()).Amm);
            Assert.AreEqual(after.Balance, quote.PoolAfter.Balance, $"{name}: pool balance of the first asset");
            Assert.AreEqual(after.Balance2, quote.PoolAfter.Balance2, $"{name}: pool balance of the second asset");
            Assert.AreEqual(after.LpTokenBalance, quote.PoolAfter.LpTokenBalance, $"{name}: LP token balance");
        }

        Assert.IsTrue(accepted >= steps.Length / 2, $"only {accepted} of {steps.Length} steps went through");
    }

    private static async Task<XrplNumber> LpTokensOf(XrplWallet account, IssuedCurrency asset, IssuedCurrency asset2)
    {
        AMMInfoResponse info = await client.AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2, Account = account.ClassicAddress }).Typed();
        return XrplNumber.Parse(info.Amm.LPTokenBalance.Value);
    }

    private async Task TrustAndFund(XrplWallet account, string amount)
    {
        TrustSet trust = await client.Autofill(new TrustSet
        {
            Account = account.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = CurrencyCode, Issuer = walletIssuer.ClassicAddress, Value = "1000000000" },
        });
        AssertSuccess(await client.SubmitAndWait(trust, account, true), "TrustSet");

        Payment payment = await client.Autofill(new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = account.ClassicAddress,
            Amount = new Currency { CurrencyCode = CurrencyCode, Issuer = walletIssuer.ClassicAddress, Value = amount },
        });
        AssertSuccess(await client.SubmitAndWait(payment, walletIssuer, true), "Payment");
    }

}
