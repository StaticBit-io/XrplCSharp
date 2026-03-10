using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Utils;
using Offer = Xrpl.Models.Transactions.Offer;

namespace XrplTests.Xrpl.Models;

[TestClass]
public class TestTradeSimulator
{
    private static AMMInfo CreateTestAmm() => new AMMInfo
    {
        Account = "rHUpaqUPbwzKZdzQ8ZQCme18FrgW9pB4am",
        Amount = new Currency { CurrencyCode = "XRP", Value = "2483087309" },
        Amount2 = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "3660.473653346136" },
        LPTokenBalance = new Currency { CurrencyCode = "03930D02208264E2E40EC1B0C09E4DB96EE197B1", Issuer = "rHUpaqUPbwzKZdzQ8ZQCme18FrgW9pB4am", Value = "2455986.915952765" },
        TradingFee = 219,
    };

    private static List<Offer> CreateTestOrderBook() => new List<Offer>
    {
        new Offer
        {
            Account = "rAccount1",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "3437371" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "5.1395571192" },
            Quality = 0.0000014952m,
            OwnerFunds = "30306580",
        },
        new Offer
        {
            Account = "rAccount2",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "20810169" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "31.15565376886572" },
            Quality = 0.00000149713602849m,
            OwnerFunds = "1226669640",
        },
        new Offer
        {
            Account = "rAccount3",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "33000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "49.471983" },
            Quality = 0.000001499151m,
            OwnerFunds = "392790626",
        },
        new Offer
        {
            Account = "rAccount4",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "180167000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "270695.6205902" },
            Quality = 0.0000015024706m,
            OwnerFunds = "180259769064",
        },
        new Offer
        {
            Account = "rAccount5",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "63987860993" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "96139.93556843851" },
            Quality = 0.000001502471470002034m,
            OwnerFunds = "174607651237",
        },
        new Offer
        {
            Account = "rAccount6",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "89583005390" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "135286.8266081912" },
            Quality = 0.000001510184058005416m,
        },
        new Offer
        {
            Account = "rAccount7",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "125000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "201.875" },
            Quality = 0.000001615m,
            OwnerFunds = "125998581",
        },
    };

    private static List<Offer> CreateTestOrderBookWithUnfunded()
    {
        var offers = CreateTestOrderBook();
        offers.Insert(1, new Offer
        {
            Account = "rUnfunded1",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "5000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "7.5" },
            Quality = 0.0000015m,
            OwnerFunds = "0",
        });
        offers.Insert(3, new Offer
        {
            Account = "rUnfunded2",
            TakerGets = new Currency { CurrencyCode = "XRP", Value = "10000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "15" },
            Quality = 0.0000015m,
            OwnerFunds = "0",
        });
        return offers;
    }

    [TestMethod]
    public void TestSwapAmount1ForAmount2()
    {
        var amm = CreateTestAmm();
        var originalAmount = amm.Amount.ValueAsNumber;
        var originalAmount2 = amm.Amount2.ValueAsNumber;

        var result = amm.SwapAmount1ForAmount2(10_000_000m);

        Assert.IsTrue(result.AmountOut > 0, "AmountOut should be > 0");
        Assert.IsTrue(result.Fee > 0, "Fee should be > 0");
        Assert.AreEqual(10_000_000m, result.AmountIn);
        Assert.IsTrue(result.UpdatedPool.Amount.ValueAsNumber > originalAmount);
        Assert.IsTrue(result.UpdatedPool.Amount2.ValueAsNumber < originalAmount2);

        Assert.AreEqual(originalAmount, amm.Amount.ValueAsNumber, "Original pool Amount should not be mutated");
        Assert.AreEqual(originalAmount2, amm.Amount2.ValueAsNumber, "Original pool Amount2 should not be mutated");
    }

    [TestMethod]
    public void TestSwapAmount2ForAmount1()
    {
        var amm = CreateTestAmm();
        var result = amm.SwapAmount2ForAmount1(10m);

        Assert.IsTrue(result.AmountOut > 0, "AmountOut should be > 0 drops");
        Assert.AreEqual(10m, result.AmountIn);
        Assert.IsTrue(result.Fee > 0);
    }

    [TestMethod]
    public void TestSwapPreservesInvariant()
    {
        var amm = CreateTestAmm();
        var originalK = amm.Amount.ValueAsNumber * amm.Amount2.ValueAsNumber;

        var result = amm.SwapAmount1ForAmount2(10_000_000m);
        var newK = result.UpdatedPool.Amount.ValueAsNumber * result.UpdatedPool.Amount2.ValueAsNumber;

        Assert.IsTrue(newK >= originalK, $"Invariant should increase or stay the same due to fees. Original: {originalK}, New: {newK}");
    }

    [TestMethod]
    public void TestGetEffectiveAmmPrice()
    {
        var amm = CreateTestAmm();
        var price = amm.GetEffectiveAmmPrice();

        var expected = 3660.473653346136m / 2483087309m;
        Assert.IsTrue(Math.Abs(price - expected) < 1e-15m, $"Expected ~{expected}, got {price}");
    }

    [TestMethod]
    public void TestSpotPrice()
    {
        var amm = CreateTestAmm();
        var spot = amm.SpotPrice();

        var expected = 2483087309m / 3660.473653346136m;
        Assert.IsTrue(Math.Abs(spot - expected) < 1m, $"Expected ~{expected}, got {spot}");
    }

    [TestMethod]
    public void TestClonePoolDoesNotMutateOriginal()
    {
        var amm = CreateTestAmm();
        var originalAmount = amm.Amount.ValueAsNumber;
        var originalAmount2 = amm.Amount2.ValueAsNumber;
        var originalLp = amm.LPTokenBalance.ValueAsNumber;

        amm.SwapAmount1ForAmount2(50_000_000m);

        Assert.AreEqual(originalAmount, amm.Amount.ValueAsNumber);
        Assert.AreEqual(originalAmount2, amm.Amount2.ValueAsNumber);
        Assert.AreEqual(originalLp, amm.LPTokenBalance.ValueAsNumber);
    }

    [TestMethod]
    public void TestSimulateTrade_BothSources()
    {
        var amm = CreateTestAmm();
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, amm, 500_000_000_000m, true);

        Assert.IsTrue(result.TotalReceived > 0, "TotalReceived should be > 0");
        Assert.IsTrue(result.FromOrderBook > 0, $"FromOrderBook should be > 0, got {result.FromOrderBook}");
        Assert.IsTrue(result.FromAmm > 0, $"FromAmm should be > 0, got {result.FromAmm}");
        Assert.IsTrue(result.AmmPoolFee > 0, "AmmPoolFee should be > 0");
        Assert.IsTrue(result.Steps.Count > 0, "Steps should not be empty");
        Assert.IsTrue(result.PriceImpactPercent != 0, $"PriceImpactPercent should be non-zero for large trade, got {result.PriceImpactPercent}");
    }

    [TestMethod]
    public void TestSimulateTrade_OnlyOrderBook()
    {
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, null, 50_000_000m, true);

        Assert.AreEqual(0m, result.FromAmm);
        Assert.IsTrue(result.FromOrderBook > 0);
        Assert.AreEqual(0m, result.AmmPoolFee);
        Assert.IsNull(result.UpdatedAmm);
    }

    [TestMethod]
    public void TestSimulateTrade_OnlyAmm()
    {
        var amm = CreateTestAmm();

        var result = TradeSimulator.SimulateTrade(new List<Offer>(), amm, 50_000_000m, true);

        Assert.AreEqual(0m, result.FromOrderBook);
        Assert.IsTrue(result.FromAmm > 0);
        Assert.IsTrue(result.AmmPoolFee > 0);
    }

    [TestMethod]
    public void TestSimulateTrade_UnfundedOffersSkipped()
    {
        var amm = CreateTestAmm();
        var funded = CreateTestOrderBook();
        var withUnfunded = CreateTestOrderBookWithUnfunded();

        var resultFunded = TradeSimulator.SimulateTrade(funded, null, 20_000_000m, true);
        var resultUnfunded = TradeSimulator.SimulateTrade(withUnfunded, null, 20_000_000m, true);

        Assert.AreEqual(resultFunded.FromOrderBook, resultUnfunded.FromOrderBook,
            "Unfunded offers should be filtered out; results should match");
    }

    [TestMethod]
    public void TestSimulateTrade_ZeroAmount()
    {
        var amm = CreateTestAmm();
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, amm, 0m, true);

        Assert.AreEqual(0m, result.TotalReceived);
        Assert.AreEqual(0, result.Steps.Count);
    }

    [TestMethod]
    public void TestSimulateTrade_LargeAmount()
    {
        var amm = CreateTestAmm();
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, amm, 999_999_999_999_999m, true);

        Assert.IsTrue(result.TotalReceived > 0, "Should produce output for large amount");
        Assert.IsTrue(result.TotalSpent > 0, "Should spend something");
    }

    [TestMethod]
    public void TestSimulateTrade_PriceImpact()
    {
        var amm = CreateTestAmm();
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, amm, 500_000_000_000m, true);

        Assert.IsTrue(result.PriceImpactPercent != 0, $"PriceImpactPercent should be non-zero for large trades, got {result.PriceImpactPercent}");
    }

    [TestMethod]
    public void TestSimulateTrade_EffectivePrice()
    {
        var amm = CreateTestAmm();
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, amm, 500_000_000_000m, true);

        var expected = result.TotalReceived / result.TotalSpent;
        Assert.AreEqual(expected, result.EffectivePrice, "EffectivePrice should equal TotalReceived / TotalSpent");
    }

    [TestMethod]
    public void TestSimulateTrade_AmmBetterThanBook()
    {
        var amm = CreateTestAmm();

        var cheapBook = new List<Offer>
        {
            new Offer
            {
                Account = "rCheap",
                TakerGets = new Currency { CurrencyCode = "XRP", Value = "100000000" },
                TakerPays = new Currency { CurrencyCode = "USD", Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", Value = "100" },
                Quality = 0.000001m,
                OwnerFunds = "100000000",
            },
        };

        var result = TradeSimulator.SimulateTrade(cheapBook, amm, 10_000_000m, true);

        Assert.IsTrue(result.FromAmm > 0, $"AMM should be used when it offers a better price than book, FromAmm={result.FromAmm}");
        Assert.IsTrue(result.Steps.Exists(s => s.Source == FillStep.SourceType.Amm),
            "Should have AMM steps when AMM price is better than book for seller");
    }

    [TestMethod]
    public void TestSimulateTrade_StepPriceDirection()
    {
        var amm = CreateTestAmm();
        var orderBook = CreateTestOrderBook();

        var result = TradeSimulator.SimulateTrade(orderBook, amm, 500_000_000_000m, true);

        foreach (var step in result.Steps)
        {
            if (step.AmountIn > 0 && step.AmountOut > 0)
            {
                Assert.AreEqual(step.AmountOut / step.AmountIn, step.Price, 1e-15m,
                    $"Step price should be AmountOut/AmountIn (taker_pays/taker_gets direction) for {step.Source}");
            }
        }
    }

    [TestMethod]
    public void TestSimulateTrade_RemainingOffers()
    {
        var orderBook = CreateTestOrderBook();
        var originalCount = orderBook.Count;

        var result = TradeSimulator.SimulateTrade(orderBook, null, 200_000_000m, true);

        Assert.IsTrue(result.RemainingOffers.Count < originalCount,
            $"RemainingOffers ({result.RemainingOffers.Count}) should be fewer than original ({originalCount})");
    }
}
