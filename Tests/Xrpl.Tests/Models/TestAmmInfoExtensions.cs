using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Utils;

namespace XrplTests.Xrpl.Models;

[TestClass]
public class TestAmmInfoExtensions
{
    private static AMMInfo GetTestPool()
    {
        return new AMMInfo
        {
            Amount = new Currency() { ValueAsNumber = 1082653882056m },
            Amount2 = new Currency() { ValueAsNumber = 923.1775709037902m, CurrencyCode = "MAG", Issuer = "rXmagwMmnFtVet3uL26Q2iwk287SRvVMJ" },
            LPTokenBalance = new Currency()
            {
                ValueAsNumber = 26806748.53473101m,
                CurrencyCode = "03C3BE9DA15E4DD7E7BDCB7A245ADDE14FD27637",
                Issuer = "rNZ2ZVF1ZU34kFQvcN4xkFAvdSvve5bXce",
            },
            TradingFee = 795,
        };
    }

    [TestMethod]
    public void TestPriceAmount2InAmount()
    {
        var pool = GetTestPool();
        var price = pool.PriceForOneAmount2InAmount();
        Assert.IsNotNull(price);
        Assert.IsTrue(price.GetValue() > 0);
    }

    [TestMethod]
    public void TestPriceAmountInAmount2()
    {
        var pool = GetTestPool();
        var price = pool.PriceForOneAmountInAmount2();
        Assert.IsNotNull(price);
        Assert.IsTrue(price.GetValue() > 0);
    }

    [TestMethod]
    public void TestInvariantK()
    {
        var pool = GetTestPool();
        var k = pool.InvariantK();
        Assert.AreEqual(pool.Amount.ValueAsNumber * pool.Amount2.ValueAsNumber, k);
    }

    [TestMethod]
    public void TestMintLpSingleAssetAmount()
    {
        var pool = GetTestPool();
        var delta = new Currency() { CurrencyCode = "XRP", ValueAsXrp = 1000 };
        var lp = pool.MintLpSingleAssetAmount(delta);
        Assert.IsTrue(lp > 0);
    }

    [TestMethod]
    public void TestMintLpSingleAssetAmount2()
    {
        var pool = GetTestPool();
        var delta = new Currency()
        {
            CurrencyCode = "MAG",
            Issuer = "rXmagwMmnFtVet3uL26Q2iwk287SRvVMJ",
            ValueAsNumber = 5
        };
        var lp = pool.MintLpSingleAssetAmount2(delta);
        Assert.IsTrue(lp > 0);
    }

    [TestMethod]
    public void TestMintLpDual()
    {
        var pool = GetTestPool();
        var dA = new Currency() { CurrencyCode = "XRP", ValueAsXrp = 1172.835991m };
        var dA2 = new Currency()
        {
            CurrencyCode = "MAG",
            Issuer = "rXmagwMmnFtVet3uL26Q2iwk287SRvVMJ",
            ValueAsNumber = 1
        };
        var lp = pool.MintLpDual(dA, dA2);
        Assert.IsTrue(lp > 0);
    }

    [TestMethod]
    public void TestMintLpDualProportional()
    {
        var pool = GetTestPool();
        var xrp = new Currency() { ValueAsNumber = 1172835991m };
        var mag = new Currency()
        {
            CurrencyCode = "MAG",
            Issuer = "rXmagwMmnFtVet3uL26Q2iwk287SRvVMJ",
            ValueAsNumber = 1
        };
        var lp = pool.MintLpDualProportional(xrp, mag);
        Assert.IsTrue(lp > 0);
    }

    [TestMethod]
    public void TestRedeemDual()
    {
        var pool = GetTestPool();
        var (dX, dA) = pool.RedeemDual(1000m);
        Assert.IsTrue(dX.ValueAsNumber > 0);
        Assert.IsTrue(dA.ValueAsNumber > 0);
    }

    [TestMethod]
    public void TestRedeemSingleToAmount2()
    {
        var pool = GetTestPool();
        var a = pool.RedeemSingleToAmount2(1000m);
        Assert.IsTrue(a.ValueAsNumber > 0);
    }

    [TestMethod]
    public void TestRedeemSingleToAmount()
    {
        var pool = GetTestPool();
        var x = pool.RedeemSingleToAmount(1000m);
        Assert.IsTrue(x.ValueAsNumber > 0);
    }

    [TestMethod]
    public void TestBurnLpForExactAmount2_Roundtrip()
    {
        var pool = GetTestPool();
        var desiredAmount2 = new Currency()
        {
            CurrencyCode = pool.Amount2.CurrencyCode,
            Issuer = pool.Amount2.Issuer,
            ValueAsNumber = 5
        };
        var lp = pool.BurnLpForExactAmountOut(desiredAmount2);
        var actual = pool.RedeemSingleToAmount2(lp);
        Assert.IsTrue(Math.Abs(actual.ValueAsNumber - desiredAmount2.ValueAsNumber) < 1e-9m);
    }

    [TestMethod]
    public void TestBurnLpForExactAmount_Roundtrip()
    {
        var pool = GetTestPool();
        var desiredAmount = new Currency()
        {
            CurrencyCode = pool.Amount.CurrencyCode,
            Issuer = pool.Amount.Issuer,
            ValueAsXrp = 100
        };
        var lp = pool.BurnLpForExactAmountOut(desiredAmount);
        var actual = pool.RedeemSingleToAmount(lp);
        Assert.IsNotNull(actual.ValueAsXrp);
        Assert.IsTrue(Math.Abs(actual.ValueAsXrp.Value - desiredAmount.ValueAsXrp.Value) < 0.01m);
    }

    [TestMethod]
    public void TestRedeemDual_ExceedingLp_Throws()
    {
        var pool = GetTestPool();
        Helper.ThrowsException<ArgumentOutOfRangeException>(() => pool.RedeemDual(pool.LPTokenBalance.ValueAsNumber + 1));
    }

    [TestMethod]
    public void TestFeeBpsToDecimal()
    {
        Assert.AreEqual(0.00795m, AmmInfoExtensions.FeeBpsToDecimal(795));
        Assert.AreEqual(0m, AmmInfoExtensions.FeeBpsToDecimal(0));
        Assert.AreEqual(1m, AmmInfoExtensions.FeeBpsToDecimal(100000));
    }

    [TestMethod]
    public void TestBurnLpForExactAmountOut_UnknownCurrency_Throws()
    {
        var pool = GetTestPool();
        var unknown = new Currency()
        {
            CurrencyCode = "USD",
            Issuer = "rUnknown",
            ValueAsNumber = 1
        };
        Helper.ThrowsException<ArgumentOutOfRangeException>(() => pool.BurnLpForExactAmountOut(unknown));
    }
}
