using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;
using static XrplTests.Xrpl.Amounts.SyntheticDexSnapshot;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// The index a snapshot is built into once and shared by every evaluation against it (issue
/// 301): a result through the shared index must be the one a freshly built index gives, whatever
/// was evaluated against the snapshot before and whatever runs beside it.
/// </summary>
[TestClass]
public class TestUDexIndex
{
    private const string Carol = "carol";

    private static Payment Pay(string from, string to, XrplAmount amount, XrplAmount? sendMax = null) =>
        new Payment
        {
            Account = from,
            Destination = to,
            Amount = amount.ToCurrency(),
            SendMax = sendMax?.ToCurrency(),
            Fee = new Currency { Value = "10" },
        };

    /// <summary>
    /// Transactions that read the books both ways, fail for want of funds, and create an account
    /// the next one must not find: the snapshot never had it.
    /// </summary>
    private static IReadOnlyList<(string Name, Func<DexSnapshot, object> Evaluate)> Transactions { get; } =
        new (string, Func<DexSnapshot, object>)[]
        {
            ("XRP for USD through the book", s => PaymentFlow.Evaluate(s, Pay(Alice, Bob, Iou(Gateway(0), "250"), Drops(1_000_000_000_000)))),
            ("USD for XRP through the book", s => PaymentFlow.Evaluate(s, Pay("owner00001", Bob, Drops(5_000_000_000), Iou(Gateway(1), "1000")))),
            ("XRP creating carol", s => PaymentFlow.Evaluate(s, Pay(Alice, Carol, Drops(50_000_000)))),
            ("USD to carol", s => PaymentFlow.Evaluate(s, Pay(Alice, Carol, Iou(Gateway(0), "10"), Drops(1_000_000_000)))),
            ("offer crossing", s => OfferCreateCrossing.Cross(s, Alice, Iou(Gateway(1), "250"), Drops(100_000_000_000), 10)),
            ("offer left on the book", s => OfferCreateCrossing.Cross(s, Alice, Iou(Gateway(2), "1"), Drops(1), 10)),
            ("unfunded offer", s => OfferCreateCrossing.Cross(s, Bob, Drops(100_000_000), Iou(Gateway(2), "50"), 10)),
        };

    /// <summary>The same lists under a new instance, which gets an index of its own.</summary>
    private static DexSnapshot Fresh(DexSnapshot snapshot) =>
        new DexSnapshot
        {
            ParentCloseTime = snapshot.ParentCloseTime,
            ReserveBase = snapshot.ReserveBase,
            ReserveIncrement = snapshot.ReserveIncrement,
            Accounts = snapshot.Accounts,
            TrustLines = snapshot.TrustLines,
            Offers = snapshot.Offers,
        };

    [TestMethod]
    public void TheIndexIsBuiltOncePerSnapshot()
    {
        DexSnapshot snapshot = Build(gateways: 2, owners: 10, offersPerBook: 5);

        Assert.AreSame(DexIndex.Of(snapshot), DexIndex.Of(snapshot));
        Assert.AreSame(DexIndex.Of(snapshot), new DexWorld(snapshot, Alice, 10).Index);
        Assert.AreNotSame(DexIndex.Of(snapshot), DexIndex.Of(Fresh(snapshot)));
    }

    [TestMethod]
    public void ResultsThroughTheSharedIndexMatchAFreshOne()
    {
        DexSnapshot shared = Build(gateways: 3, owners: 50, offersPerBook: 40);

        // Twice over, so every transaction runs after every other has touched the shared index.
        for (int round = 0; round < 2; round++)
        {
            foreach ((string name, Func<DexSnapshot, object> evaluate) in Transactions)
                Assert.AreEqual(Describe(evaluate(Fresh(shared))), Describe(evaluate(shared)), $"{name}, round {round}");
        }
    }

    [TestMethod]
    public void AnAccountAPaymentCreatesStaysInItsEvaluation()
    {
        DexSnapshot snapshot = Build(gateways: 1, owners: 10, offersPerBook: 5);

        Assert.AreEqual("tesSUCCESS", PaymentFlow.Evaluate(snapshot, Pay(Alice, Carol, Drops(50_000_000))).EngineResult);
        Assert.AreEqual("tecNO_DST", PaymentFlow.Evaluate(snapshot, Pay(Alice, Carol, Iou(Gateway(0), "10"), Drops(1_000_000_000))).EngineResult);
        Assert.IsFalse(DexIndex.Of(snapshot).Accounts.ContainsKey(Carol));
    }

    [TestMethod]
    public void ConcurrentEvaluationsDoNotSeeEachOther()
    {
        DexSnapshot snapshot = Build(gateways: 3, owners: 50, offersPerBook: 40);
        string[] expected = Transactions.Select(t => Describe(t.Evaluate(Fresh(snapshot)))).ToArray();

        string[] actual = new string[Transactions.Count * 16];
        Parallel.For(0, actual.Length, i => actual[i] = Describe(Transactions[i % Transactions.Count].Evaluate(snapshot)));

        for (int i = 0; i < actual.Length; i++)
            Assert.AreEqual(expected[i % Transactions.Count], actual[i], Transactions[i % Transactions.Count].Name);
    }

    [TestMethod]
    public void ASnapshotThatFailsToIndexFailsEveryTime()
    {
        DexSnapshot snapshot = new DexSnapshot
        {
            Accounts = new[] { new DexAccount { Address = Alice }, new DexAccount { Address = Gateway(0) } },
            TrustLines = new[]
            {
                new DexTrustLine { Account = Alice, Balance = Iou(Gateway(0), "100") },
                new DexTrustLine { Account = Gateway(0), Balance = XrplAmount.Parse(Usd(Alice), "-100") },
            },
        };

        Assert.ThrowsExactly<ArgumentException>(() => DexIndex.Of(snapshot));
        Assert.ThrowsExactly<ArgumentException>(() => DexIndex.Of(snapshot));
    }

    /// <summary>Every public property of a result, nested, as text: two results are the same when their texts are.</summary>
    private static string Describe(object value)
    {
        StringBuilder text = new StringBuilder();
        Describe(value, text, 0);
        return text.ToString();
    }

    private static void Describe(object value, StringBuilder text, int depth)
    {
        if (value == null)
        {
            text.Append("null");
            return;
        }

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || depth > 6 ||
            type.GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType == type)
        {
            text.Append(value);
            return;
        }

        if (value is IEnumerable items)
        {
            text.Append('[');
            foreach (object item in items)
            {
                Describe(item, text, depth + 1);
                text.Append(", ");
            }

            text.Append(']');
            return;
        }

        text.Append('{');
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            text.Append(property.Name).Append('=');
            Describe(property.GetValue(value), text, depth + 1);
            text.Append("; ");
        }

        text.Append('}');
    }
}
