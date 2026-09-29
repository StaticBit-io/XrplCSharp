using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <see cref="OfferCrossing"/> against the node: a maker places an offer, a taker's
/// <c>OfferCreate</c> crosses part of it, and what the step predicts - what the taker receives,
/// what the maker's owner pays, what is left of the offer - is compared with the transaction's
/// metadata to the last digit. Also <see cref="BalanceChanges.GetBalanceChanges"/> on a balance
/// far beyond <see cref="decimal"/>.
/// </summary>
[TestClass]
public class TestIOfferCrossing
{
    private static IXrplClient client;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;
    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task Cross_IouForXrp_Partially()
    {
        // 100 USD for 33 XRP: the taker wants 12.3456789 USD, so the XRP it pays is rounded.
        await Cross(transferRate: null, makerFunds: "1000", offerGets: "100", offerPaysDrops: 33_000_000, takerWants: "12.3456789", takerGivesDrops: 10_000_000);
    }

    [TestMethod]
    public async Task Cross_IouForXrp_WithATransferFee()
    {
        // The maker, not the issuer, pays the 0.2% transfer fee on the USD it delivers.
        await Cross(transferRate: 1_002_000_000, makerFunds: "1000", offerGets: "100", offerPaysDrops: 33_000_000, takerWants: "12.3456789", takerGivesDrops: 10_000_000);
    }

    [TestMethod]
    public async Task Cross_IouForXrp_MakerHoldsLessThanTheOffer()
    {
        // The maker offers 100 USD holding 40: the taker gets what the maker can pay.
        await Cross(transferRate: null, makerFunds: "40", offerGets: "100", offerPaysDrops: 33_000_000, takerWants: "60", takerGivesDrops: 50_000_000);
    }

    [TestMethod]
    public async Task Cross_IouForIou_TwoIssuers()
    {
        XrplWallet usdIssuer = XrplWallet.Generate();
        XrplWallet eurIssuer = XrplWallet.Generate();
        XrplWallet maker = XrplWallet.Generate();
        XrplWallet taker = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, usdIssuer, eurIssuer, maker, taker);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = usdIssuer.ClassicAddress };
        IssuedCurrency eur = new IssuedCurrency { Currency = "EUR", Issuer = eurIssuer.ClassicAddress };
        await DefaultRipple(usdIssuer, null);
        await DefaultRipple(eurIssuer, null);
        await TrustAndFund(eurIssuer, maker, eur, "1000");
        await TrustAndFund(usdIssuer, taker, usd, "1000");
        await Trust(maker, usd);
        await Trust(taker, eur);

        // The maker sells 77.77 EUR for 123.456 USD; the taker wants 11.1111 EUR.
        LOOffer offer = await PlaceOffer(maker, Iou(eur, "77.77"), Iou(usd, "123.456"));
        XrplAmount wants = XrplAmount.Parse(eur, "11.1111");
        XrplAmount gives = XrplAmount.Parse(usd, "50");

        OfferStep predicted = await Predict(offer, ownerFunds: XrplAmount.Parse(eur, "1000"), 1_000_000_000, 1_000_000_000, wants, gives);
        Dictionary<string, List<Currency>> changes = await TakeOffer(taker, wants, gives);

        AssertChange(changes, taker.ClassicAddress, predicted.StepOut);
        AssertChange(changes, maker.ClassicAddress, -predicted.OwnerGives);
        AssertChange(changes, maker.ClassicAddress, predicted.OfferIn);
        await AssertOfferLeft(maker, offer, predicted);
    }

    [TestMethod]
    public async Task GetBalanceChanges_BalanceBeyondDecimal()
    {
        // 5e90 USD is a valid ledger amount and 67 orders of magnitude past decimal.
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet holder = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = issuer.ClassicAddress };
        await Trust(holder, usd, "1e95");

        Payment payment = await client.Autofill(new Payment
        {
            Account = issuer.ClassicAddress,
            Destination = holder.ClassicAddress,
            Amount = new Currency { CurrencyCode = "USD", Issuer = issuer.ClassicAddress, Value = "5e90" },
        });
        TransactionSummary result = await client.SubmitAndWait(payment, issuer, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult);

        Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(result.Meta);

        AccountLines lines = await client.AccountLines(new AccountLinesRequest(holder.ClassicAddress)).Typed();
        XrplAmount onLedger = XrplAmount.Parse(usd, lines.TrustLines.Single().Balance);
        Currency received = changes[holder.ClassicAddress].Single(c => c.CurrencyCode == "USD");
        Assert.AreEqual(onLedger, received.ToXrplAmount(), "the holder's change is its whole new balance");
        Assert.AreEqual("5000000000000000e75", received.Value);
        Assert.AreEqual("-5000000000000000e75", changes[issuer.ClassicAddress].Single(c => c.CurrencyCode == "USD").Value);
    }

    private static async Task Cross(uint? transferRate, string makerFunds, string offerGets, long offerPaysDrops, string takerWants, long takerGivesDrops)
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet maker = XrplWallet.Generate();
        XrplWallet taker = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, maker, taker);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = issuer.ClassicAddress };
        await DefaultRipple(issuer, transferRate);
        await TrustAndFund(issuer, maker, usd, makerFunds);
        await Trust(taker, usd);

        LOOffer offer = await PlaceOffer(maker, Iou(usd, offerGets), new Currency { Value = offerPaysDrops.ToString() });
        XrplAmount wants = XrplAmount.Parse(usd, takerWants);
        XrplAmount gives = XrplAmount.FromNumber(Xrp, (XrplNumber)takerGivesDrops);

        uint rateOut = transferRate ?? OfferCrossing.QualityOne;
        OfferStep predicted = await Predict(offer, XrplAmount.Parse(usd, makerFunds), OfferCrossing.QualityOne, rateOut, wants, gives);
        Dictionary<string, List<Currency>> changes = await TakeOffer(taker, wants, gives);

        AssertChange(changes, taker.ClassicAddress, predicted.StepOut);
        AssertChange(changes, maker.ClassicAddress, -predicted.OwnerGives);
        AssertChange(changes, maker.ClassicAddress, predicted.OfferIn);
        await AssertOfferLeft(maker, offer, predicted);
    }

    /// <summary>The step a taker's <c>OfferCreate</c> makes on one offer: both passes of its book step.</summary>
    private static async Task<OfferStep> Predict(LOOffer offer, XrplAmount ownerFunds, uint rateIn, uint rateOut, XrplAmount wants, XrplAmount gives)
    {
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        XrplQuality quality = XrplQuality.FromBookDirectory(offer.BookDirectory);
        XrplAmount offerIn = offer.TakerPays.ToXrplAmount();
        XrplAmount offerOut = offer.TakerGets.ToXrplAmount();

        OfferStep funded = OfferCrossing.Fund(quality, offerIn, offerOut, ownerFunds, rateIn, rateOut, rules);
        return OfferCrossing.Cross(funded, wants, gives, rules);
    }

    private static async Task<Dictionary<string, List<Currency>>> TakeOffer(XrplWallet taker, XrplAmount wants, XrplAmount gives)
    {
        OfferCreate take = await client.Autofill(new OfferCreate
        {
            Account = taker.ClassicAddress,
            TakerPays = wants.ToCurrency(),
            TakerGets = gives.ToCurrency(),
            Flags = OfferCreateFlags.tfImmediateOrCancel,
        });
        TransactionSummary result = await client.SubmitAndWait(take, taker, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult, "the taker's offer crosses");
        return BalanceChanges.GetBalanceChanges(result.Meta);
    }

    private static void AssertChange(Dictionary<string, List<Currency>> changes, string account, XrplAmount expected)
    {
        Assert.IsTrue(changes.TryGetValue(account, out List<Currency> list), $"{account} has no balance change");
        XrplAmount[] actual = list
            .Select(c => c.ToXrplAmount())
            .Where(a => XrplAmount.SameAsset(a.Asset, expected.Asset) && a.IsNegative == expected.IsNegative)
            .ToArray();
        Assert.HasCount(1, actual, $"{account}: one change in {expected.Asset}");
        Assert.AreEqual(expected, actual[0], $"{account}: {expected} predicted, {actual[0]} on the ledger");
    }

    private static async Task AssertOfferLeft(XrplWallet maker, LOOffer before, OfferStep predicted)
    {
        XrplAmount getsLeft = XrplAmountMath.Subtract(before.TakerGets.ToXrplAmount(), predicted.OfferOut);
        XrplAmount paysLeft = XrplAmountMath.Subtract(before.TakerPays.ToXrplAmount(), predicted.OfferIn);

        LOOffer after = await ReadOffer(maker.ClassicAddress, before.Sequence.Value);
        if (after == null)
            return;

        Assert.AreEqual(getsLeft, after.TakerGets.ToXrplAmount(), "what the offer still gives");
        Assert.AreEqual(paysLeft, after.TakerPays.ToXrplAmount(), "what the offer still takes");
    }

    private static async Task<LOOffer> PlaceOffer(XrplWallet maker, Currency gets, Currency pays)
    {
        OfferCreate place = await client.Autofill(new OfferCreate
        {
            Account = maker.ClassicAddress,
            TakerGets = gets,
            TakerPays = pays,
        });
        TransactionSummary result = await client.SubmitAndWait(place, maker, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult, "the maker's offer is placed");

        LOOffer offer = await ReadOffer(maker.ClassicAddress, place.Sequence.Value);
        Assert.IsNotNull(offer, "the maker's offer is on the ledger");
        return offer;
    }

    private static async Task<LOOffer> ReadOffer(string account, uint sequence)
    {
        try
        {
            LedgerEntryRequest request = new LedgerEntryRequest { Offer = new OfferQuery { Account = account, Seq = sequence } };
            return (LOOffer)(await client.LedgerEntry(request).Typed()).Node;
        }
        catch (RippledException exception) when (exception.Response?.Error == XrplErrorCodes.EntryNotFound)
        {
            return null;
        }
    }

    private static Currency Iou(IssuedCurrency asset, string value) =>
        new Currency { CurrencyCode = asset.Currency, Issuer = asset.Issuer, Value = value };

    private static async Task DefaultRipple(XrplWallet issuer, uint? transferRate)
    {
        AccountSet set = await client.Autofill(new AccountSet
        {
            Account = issuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple,
            TransferRate = transferRate,
        });
        TransactionSummary result = await client.SubmitAndWait(set, issuer, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult, "AccountSet");
    }

    private static async Task Trust(XrplWallet holder, IssuedCurrency asset, string limit = "1000000000")
    {
        TrustSet trust = await client.Autofill(new TrustSet
        {
            Account = holder.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = asset.Currency, Issuer = asset.Issuer, Value = limit },
        });
        TransactionSummary result = await client.SubmitAndWait(trust, holder, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult, "TrustSet");
    }

    private static async Task TrustAndFund(XrplWallet issuer, XrplWallet holder, IssuedCurrency asset, string amount)
    {
        await Trust(holder, asset);
        Payment payment = await client.Autofill(new Payment
        {
            Account = issuer.ClassicAddress,
            Destination = holder.ClassicAddress,
            Amount = new Currency { CurrencyCode = asset.Currency, Issuer = asset.Issuer, Value = amount },
        });
        TransactionSummary result = await client.SubmitAndWait(payment, issuer, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult, "Payment");
    }
}
