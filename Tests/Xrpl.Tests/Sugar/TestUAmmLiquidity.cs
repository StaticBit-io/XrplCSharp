using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestUAmmLiquidity
{
    private static readonly IssuedCurrency Usd = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" };
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static readonly AmmPool Pool = new AmmPool
    {
        Asset = Usd,
        Asset2 = Xrp,
        Balance = XrplNumber.Parse("1000"),
        Balance2 = XrplNumber.Parse("100000000"),
        LpTokenBalance = XrplNumber.Parse("316227.766016838"),
        TradingFee = 500,
    };

    [TestMethod]
    [DataRow("10", "0")]
    [DataRow("10", "-1")]
    [DataRow("-10", "1")]
    public void EffectivePrice_NotPositive_IsTheBadAmountPreflightReturns(string amount, string price)
    {
        // AMMDeposit and AMMWithdraw preflight refuse an EPrice that is not positive, and a
        // negative Amount, before any equation runs (invalidAMMAmount).
        AmmQuote deposit = AmmLiquidity.DepositWithEffectivePrice(Pool, Usd, XrplNumber.Parse(amount), XrplNumber.Parse(price));
        AmmQuote withdraw = AmmLiquidity.WithdrawWithEffectivePrice(Pool, Usd, XrplNumber.Parse(amount), XrplNumber.Parse(price), holderLpTokens: 1000);

        Assert.AreEqual("temBAD_AMOUNT", deposit.Refusal);
        Assert.AreEqual("temBAD_AMOUNT", withdraw.Refusal);
    }
}
