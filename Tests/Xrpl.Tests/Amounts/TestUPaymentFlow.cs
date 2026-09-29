using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// <see cref="PaymentFlow"/> against the payments rippled's own <c>Flow_test.cpp</c> pins
/// (<c>testDirectStep</c>, <c>testLineQuality</c>), rebuilt as snapshots: rippling through
/// accounts, transfer fees, the best of two paths, partial payment, the limit quality and the
/// trust lines' qualities in and out.
/// </summary>
[TestClass]
public class TestUPaymentFlow
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Carol = "carol";
    private const string Dan = "dan";
    private const string Erin = "erin";
    private const string Gw = "gw";

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private static IssuedCurrency Usd(string issuer) => new IssuedCurrency { Currency = "USD", Issuer = issuer };

    private static XrplAmount Iou(string issuer, string value) => XrplAmount.Parse(Usd(issuer), value);

    /// <summary>A snapshot built the way a jtx test sets up its ledger: accounts funded with XRP, then trust lines and balances.</summary>
    private sealed class Ledger
    {
        private readonly List<DexAccount> _accounts = new List<DexAccount>();
        private readonly Dictionary<(string Holder, string Issuer), (string Limit, uint QualityIn, uint QualityOut, string Balance)> _lines =
            new Dictionary<(string, string), (string, uint, uint, string)>();

        internal Ledger(params string[] accounts)
        {
            foreach (string account in accounts)
                _accounts.Add(new DexAccount { Address = account, Balance = 10_000_000_000 });
        }

        internal Ledger Rate(string issuer, uint rate)
        {
            DexAccount existing = _accounts.Single(a => a.Address == issuer);
            _accounts[_accounts.IndexOf(existing)] = new DexAccount { Address = issuer, Balance = existing.Balance, TransferRate = rate };
            return this;
        }

        /// <summary><c>env.trust(issuer["USD"](limit), holder)</c>, with the holder's qualities in percent.</summary>
        internal Ledger Trust(string holder, string issuer, string limit, uint qualityInPercent = 0, uint qualityOutPercent = 0)
        {
            string balance = _lines.TryGetValue((holder, issuer), out var line) ? line.Balance : "0";
            _lines[(holder, issuer)] = (limit, qualityInPercent * 10_000_000, qualityOutPercent * 10_000_000, balance);
            return this;
        }

        /// <summary>What <paramref name="holder"/> holds of <paramref name="issuer"/>'s USD after the setup payments.</summary>
        internal Ledger Holds(string holder, string issuer, string balance)
        {
            var line = _lines[(holder, issuer)];
            _lines[(holder, issuer)] = (line.Limit, line.QualityIn, line.QualityOut, balance);
            return this;
        }

        internal DexSnapshot Snapshot()
        {
            List<DexTrustLine> lines = new List<DexTrustLine>();
            foreach (var entry in _lines)
            {
                (string holder, string issuer) = entry.Key;
                var line = entry.Value;

                // The issuer's side of the same line, when it trusts the holder back.
                _lines.TryGetValue((issuer, holder), out var back);
                if (back.Limit != null && string.CompareOrdinal(issuer, holder) < 0)
                    continue;

                lines.Add(new DexTrustLine
                {
                    Account = holder,
                    Balance = Iou(issuer, back.Limit != null && back.Balance != "0" ? "-" + back.Balance : line.Balance),
                    Limit = Iou(issuer, line.Limit),
                    PeerLimit = back.Limit != null ? Iou(issuer, back.Limit) : null,
                    QualityIn = line.QualityIn,
                    QualityOut = line.QualityOut,
                    PeerQualityIn = back.QualityIn,
                    PeerQualityOut = back.QualityOut,
                });
            }

            return new DexSnapshot
            {
                ParentCloseTime = 800_000_000,
                ReserveBase = 10_000_000,
                ReserveIncrement = 2_000_000,
                Accounts = _accounts.ToList(),
                TrustLines = lines,
            };
        }
    }

    private static Payment Pay(string from, string to, XrplAmount amount, XrplAmount? sendMax = null, PaymentFlags flags = 0, params string[][] paths) =>
        new Payment
        {
            Account = from,
            Destination = to,
            Amount = amount.ToCurrency(),
            SendMax = sendMax?.ToCurrency(),
            Flags = flags,
            Fee = new Currency { Value = "10" },
            Paths = paths.Length == 0
                ? null
                : paths.Select(path => path.Select(account => new PathStep { Account = account }).ToList()).ToList(),
        };

    /// <summary>What <paramref name="holder"/> holds of <paramref name="issuer"/>'s USD after the payment.</summary>
    private static XrplAmount After(PaymentFlowResult result, DexSnapshot snapshot, string holder, string issuer)
    {
        XrplAmount before = snapshot.TrustLines
            .Where(l => l.Account == holder && l.Balance.Asset.Issuer == issuer)
            .Select(l => l.Balance)
            .Concat(snapshot.TrustLines.Where(l => l.Account == issuer && l.Balance.Asset.Issuer == holder).Select(l => (-l.Balance).WithAssetForTest(Usd(issuer))))
            .DefaultIfEmpty(Iou(issuer, "0"))
            .First();
        XrplAmount change = result.BalanceChanges
            .Where(c => c.Account == holder && c.Change.Asset.Currency == "USD" && c.Change.Asset.Issuer == issuer)
            .Select(c => c.Change)
            .DefaultIfEmpty(Iou(issuer, "0"))
            .Single();
        return XrplAmountMath.Add(before, XrplAmount.Parse(Usd(issuer), change.ToString()));
    }

    [TestMethod]
    public void TrivialPathThroughTheIssuer()
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Gw).Trust(Alice, Gw, "1000").Trust(Bob, Gw, "1000").Holds(Alice, Gw, "100").Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, Pay(Alice, Bob, Iou(Gw, "10")));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Gw, "10"), After(result, snapshot, Bob, Gw));
    }

    [TestMethod]
    public void PartialPayment()
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Gw).Trust(Alice, Gw, "1000").Trust(Bob, Gw, "1000").Holds(Alice, Gw, "100").Snapshot();
        Assert.AreEqual("tecPATH_PARTIAL", PaymentFlow.Evaluate(snapshot, Pay(Alice, Bob, Iou(Gw, "110"))).EngineResult);

        PaymentFlowResult partial = PaymentFlow.Evaluate(snapshot, Pay(Alice, Bob, Iou(Gw, "110"), flags: PaymentFlags.tfPartialPayment));
        Assert.AreEqual("tesSUCCESS", partial.EngineResult);
        Assert.AreEqual(Iou(Gw, "100"), After(partial, snapshot, Bob, Gw));
        Assert.AreEqual(Iou(Gw, "100"), partial.DeliveredAmount);
    }

    [TestMethod]
    public void RipplingThroughAccounts()
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol, Dan).Trust(Bob, Alice, "10").Trust(Carol, Bob, "10").Trust(Dan, Carol, "10").Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, Pay(Alice, Dan, Iou(Carol, "10"), paths: new[] { Bob }));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Alice, "10"), After(result, snapshot, Bob, Alice));
        Assert.AreEqual(Iou(Bob, "10"), After(result, snapshot, Carol, Bob));
        Assert.AreEqual(Iou(Carol, "10"), After(result, snapshot, Dan, Carol));
    }

    [TestMethod]
    public void TransferFeeWhenRedeemingThenIssuing()
    {
        // alice redeems bob's USD to bob, bob issues to carol: bob's 10% is charged.
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol, Dan)
            .Trust(Bob, Alice, "10").Trust(Alice, Bob, "10").Trust(Carol, Bob, "10").Trust(Dan, Carol, "10")
            .Rate(Bob, 1_100_000_000)
            .Holds(Alice, Bob, "6")
            .Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(
            snapshot,
            Pay(Alice, Dan, Iou(Carol, "5"), Iou(Alice, "6"), PaymentFlags.tfNoDirectRipple, new[] { Bob, Carol }));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Carol, "5"), After(result, snapshot, Dan, Carol));
        Assert.AreEqual(Iou(Bob, "0.5"), After(result, snapshot, Alice, Bob));
    }

    [TestMethod]
    public void NoTransferFeeWhenTheSenderIssues()
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol, Dan)
            .Trust(Bob, Alice, "10").Trust(Alice, Bob, "10").Trust(Carol, Bob, "10").Trust(Dan, Carol, "10")
            .Rate(Bob, 1_100_000_000)
            .Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(
            snapshot,
            Pay(Alice, Dan, Iou(Carol, "5"), Iou(Alice, "6"), PaymentFlags.tfNoDirectRipple, new[] { Bob, Carol }));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Carol, "5"), After(result, snapshot, Dan, Carol));
        Assert.AreEqual(Iou(Alice, "5"), After(result, snapshot, Bob, Alice));
    }

    [TestMethod]
    public void TheBetterOfTwoPathsIsTaken()
    {
        // alice -> carol -> dan -> erin charges carol's 10%; alice -> bob -> dan -> erin does not.
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol, Dan, Erin)
            .Trust(Bob, Alice, "10").Trust(Carol, Alice, "10").Trust(Dan, Bob, "10").Trust(Alice, Carol, "10").Trust(Dan, Carol, "10").Trust(Erin, Dan, "20")
            .Rate(Carol, 1_100_000_000)
            .Holds(Alice, Carol, "10")
            .Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(
            snapshot,
            Pay(Alice, Erin, XrplAmount.Parse(Usd(Dan), "5"), flags: PaymentFlags.tfNoDirectRipple, paths: new[] { new[] { Carol, Dan }, new[] { Bob, Dan } }));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Dan, "5"), After(result, snapshot, Erin, Dan));
        Assert.AreEqual(Iou(Bob, "5"), After(result, snapshot, Dan, Bob));
        Assert.AreEqual(Iou(Carol, "0"), After(result, snapshot, Dan, Carol));
    }

    [TestMethod]
    public void LimitQuality()
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol).Trust(Bob, Alice, "10").Trust(Carol, Bob, "10").Snapshot();
        PaymentFlowResult limited = PaymentFlow.Evaluate(
            snapshot,
            Pay(Alice, Carol, Iou(Bob, "5"), Iou(Alice, "4"), PaymentFlags.tfLimitQuality | PaymentFlags.tfPartialPayment));
        Assert.AreEqual("tecPATH_DRY", limited.EngineResult);

        PaymentFlowResult partial = PaymentFlow.Evaluate(snapshot, Pay(Alice, Carol, Iou(Bob, "5"), Iou(Alice, "4"), PaymentFlags.tfPartialPayment));
        Assert.AreEqual("tesSUCCESS", partial.EngineResult);
        Assert.AreEqual(Iou(Bob, "4"), After(partial, snapshot, Carol, Bob));
    }

    /// <summary>Dan -> Bob -> Alice -> Carol, bob's quality in from dan and out to alice varied.</summary>
    [TestMethod]
    [DataRow(80u, 80u)]
    [DataRow(80u, 100u)]
    [DataRow(80u, 120u)]
    [DataRow(100u, 80u)]
    [DataRow(100u, 100u)]
    [DataRow(100u, 120u)]
    [DataRow(120u, 80u)]
    [DataRow(120u, 100u)]
    [DataRow(120u, 120u)]
    public void LineQualityThroughAnIntermediary(uint bobDanQualityIn, uint bobAliceQualityOut)
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol, Dan)
            .Trust(Bob, Dan, "100", qualityInPercent: bobDanQualityIn)
            .Trust(Bob, Alice, "100", qualityOutPercent: bobAliceQualityOut)
            .Trust(Carol, Alice, "100")
            .Holds(Bob, Alice, "100")
            .Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(
            snapshot,
            Pay(Dan, Carol, Iou(Alice, "10"), Iou(Dan, "100"), PaymentFlags.tfNoDirectRipple, new[] { Bob }));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Alice, "90"), After(result, snapshot, Bob, Alice));
        string expected = bobAliceQualityOut > bobDanQualityIn
            ? (10.0 * bobAliceQualityOut / bobDanQualityIn).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : "10";
        Assert.AreEqual(XrplAmount.Parse(Usd(Dan), expected).ToString(), After(result, snapshot, Bob, Dan).ToString());
        Assert.AreEqual(Iou(Alice, "10"), After(result, snapshot, Carol, Alice));
    }

    /// <summary>Bob -> Alice -> Carol, carol's quality in varied: a quality above 1 is capped on the last step.</summary>
    [TestMethod]
    [DataRow(80u, "3.75")]
    [DataRow(100u, "5")]
    [DataRow(120u, "5")]
    public void QualityInOfTheDestination(uint carolAliceQualityIn, string bobLeft)
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol)
            .Trust(Bob, Alice, "10").Trust(Carol, Alice, "10", qualityInPercent: carolAliceQualityIn)
            .Holds(Bob, Alice, "10")
            .Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, Pay(Bob, Carol, Iou(Alice, "5"), Iou(Alice, "10")));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Alice, bobLeft), After(result, snapshot, Bob, Alice));
    }

    /// <summary>Bob -> Alice -> Carol, bob's quality out varied: redeeming to the issuer ignores it.</summary>
    [TestMethod]
    [DataRow(80u)]
    [DataRow(100u)]
    [DataRow(120u)]
    public void QualityOutOfTheSourceIsIgnoredWhenItRedeems(uint bobAliceQualityOut)
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob, Carol)
            .Trust(Bob, Alice, "10", qualityOutPercent: bobAliceQualityOut).Trust(Carol, Alice, "10")
            .Holds(Bob, Alice, "10")
            .Snapshot();
        PaymentFlowResult result = PaymentFlow.Evaluate(snapshot, Pay(Bob, Carol, Iou(Alice, "5"), Iou(Alice, "5")));
        Assert.AreEqual("tesSUCCESS", result.EngineResult);
        Assert.AreEqual(Iou(Alice, "5"), After(result, snapshot, Carol, Alice));
        Assert.AreEqual(Iou(Alice, "5"), After(result, snapshot, Bob, Alice));
    }

    [TestMethod]
    public void MalformedPayments()
    {
        DexSnapshot snapshot = new Ledger(Alice, Bob).Snapshot();
        XrplAmount xrp = XrplAmount.Parse(Xrp, "1000000");
        Assert.AreEqual("temREDUNDANT", PaymentFlow.Evaluate(snapshot, Pay(Alice, Alice, xrp)).EngineResult);
        Assert.AreEqual("temBAD_SEND_XRP_MAX", PaymentFlow.Evaluate(snapshot, Pay(Alice, Bob, xrp, xrp)).EngineResult);
        Assert.AreEqual("temBAD_SEND_XRP_PARTIAL", PaymentFlow.Evaluate(snapshot, Pay(Alice, Bob, xrp, flags: PaymentFlags.tfPartialPayment)).EngineResult);
        Assert.IsFalse(PaymentFlow.Evaluate(snapshot, Pay(Alice, Alice, xrp)).Applied);
    }
}

internal static class TestAmountExtensions
{
    internal static XrplAmount WithAssetForTest(this XrplAmount amount, IssuedCurrency asset) =>
        XrplAmount.Parse(asset, amount.ToString());
}
