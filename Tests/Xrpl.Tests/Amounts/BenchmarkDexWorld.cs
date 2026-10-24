using System;
using System.Diagnostics;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Models.Transactions;

using static XrplTests.Xrpl.Amounts.SyntheticDexSnapshot;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// Manual benchmark of indexing a snapshot for the engine (issue 301). Deliberately named
/// outside the TestU/TestI filters so it never runs in CI; invoke it explicitly:
/// <c>dotnet test --filter "FullyQualifiedName~BenchmarkDexWorld" --logger "console;verbosity=detailed"</c>.
/// Knobs: BENCH_GATEWAYS, BENCH_OWNERS, BENCH_OFFERS_PER_BOOK, BENCH_ITERATIONS.
/// </summary>
[TestClass]
public class BenchmarkDexWorld
{
    private static int EnvInt(string name, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out int value) && value > 0 ? value : fallback;
    }

    /// <summary>The same lists under a new instance, as a caller rolling a snapshot forward hands the engine each time.</summary>
    private static DexSnapshot Rolled(DexSnapshot snapshot) =>
        new DexSnapshot
        {
            ParentCloseTime = snapshot.ParentCloseTime,
            LedgerSequence = snapshot.LedgerSequence,
            ReserveBase = snapshot.ReserveBase,
            ReserveIncrement = snapshot.ReserveIncrement,
            Accounts = snapshot.Accounts,
            TrustLines = snapshot.TrustLines,
            Offers = snapshot.Offers,
        };

    /// <summary>
    /// The issue's workload: pending transactions evaluated one after another, each against the
    /// snapshot the last one left - payments and offers across the gateways' books.
    /// </summary>
    private static DexSnapshot RunChain(DexSnapshot snapshot, int gateways, bool rebuildIndex)
    {
        for (int i = 0; i < 15; i++)
        {
            string gateway = Gateway(i % gateways);
            DexSnapshot after = i % 3 == 2
                ? OfferCreateCrossing.Cross(snapshot, Alice, Iou(gateway, "150"), Drops(100_000_000_000), 10).SnapshotAfter
                : PaymentFlow.Evaluate(snapshot, new Payment
                {
                    Account = Alice,
                    Destination = Bob,
                    Amount = Iou(gateway, "120").ToCurrency(),
                    SendMax = Drops(1_000_000_000_000).ToCurrency(),
                    Fee = new global::Xrpl.Models.Common.Currency { Value = "10" },
                }).SnapshotAfter;

            // A new instance gets an index of its own, as a snapshot rolled forward by hand does.
            snapshot = rebuildIndex ? Rolled(after) : after;
        }

        return snapshot;
    }

    [TestMethod]
    public void BenchmarkIndexAndEvaluate()
    {
        int gateways = EnvInt("BENCH_GATEWAYS", 5);
        int owners = EnvInt("BENCH_OWNERS", 1000);
        int offersPerBook = EnvInt("BENCH_OFFERS_PER_BOOK", 400);
        int iterations = EnvInt("BENCH_ITERATIONS", 300);

        DexSnapshot snapshot = Build(gateways, owners, offersPerBook);
        Payment payment = new Payment
        {
            Account = Alice,
            Destination = Bob,
            Amount = Iou(Gateway(0), "250").ToCurrency(),
            SendMax = Drops(1_000_000_000_000).ToCurrency(),
            Fee = new global::Xrpl.Models.Common.Currency { Value = "10" },
        };

        PaymentFlowResult paid = PaymentFlow.Evaluate(snapshot, payment);
        Assert.AreEqual("tesSUCCESS", paid.EngineResult, "the benchmark payment must cross the book");
        OfferCrossingResult crossed = OfferCreateCrossing.Cross(snapshot, Alice, Iou(Gateway(1), "250"), Drops(100_000_000_000), 10);
        Assert.AreEqual("tesSUCCESS", crossed.EngineResult, "the benchmark offer must cross the book");

        Console.WriteLine("=== DexWorld index benchmark ===");
        Console.WriteLine($"accounts / lines / offers : {snapshot.Accounts.Count} / {snapshot.TrustLines.Count} / {snapshot.Offers.Count}");
        Console.WriteLine($"iterations                : {iterations}");
        Console.WriteLine($"{"scenario",-34} {"mean",8} {"p50",8} {"p95",8} {"alloc/op",10}");

        Measure("index built afresh", iterations, () => new DexIndex(snapshot));
        Measure("world over the cached index", iterations, () => new DexWorld(snapshot));
        Measure("Payment, same snapshot", iterations, () => PaymentFlow.Evaluate(snapshot, payment));
        Measure("Payment, rolled snapshot", iterations, () => PaymentFlow.Evaluate(Rolled(snapshot), payment));
        Measure("OfferCreate, same snapshot", iterations,
            () => OfferCreateCrossing.Cross(snapshot, Alice, Iou(Gateway(1), "250"), Drops(100_000_000_000), 10));
        Measure("OfferCreate, rolled snapshot", iterations,
            () => OfferCreateCrossing.Cross(Rolled(snapshot), Alice, Iou(Gateway(1), "250"), Drops(100_000_000_000), 10));

        // A market maker's re-quote: an offer resting in a book, then cancelled and replaced at a new price.
        OfferCreate quote = new OfferCreate
        {
            Account = Alice,
            TakerPays = Iou(Gateway(2), "100").ToCurrency(),
            TakerGets = Drops(50_000_000).ToCurrency(),
            Fee = new global::Xrpl.Models.Common.Currency { Value = "10" },
            Sequence = 1,
        };
        OfferCrossingResult rested = OfferCreateCrossing.Cross(snapshot, quote);
        Assert.IsNotNull(rested.PlacedTakerPays, "the quote must rest");
        DexSnapshot quoted = rested.SnapshotAfter;
        OfferCreate requote = new OfferCreate
        {
            Account = Alice,
            TakerPays = Iou(Gateway(2), "100").ToCurrency(),
            TakerGets = Drops(51_000_000).ToCurrency(),
            Fee = new global::Xrpl.Models.Common.Currency { Value = "10" },
            Sequence = 2,
            OfferSequence = 1,
        };
        OfferCrossingResult replaced = OfferCreateCrossing.Cross(quoted, requote);
        Assert.IsTrue(replaced.Offers.Any(o => o.Deleted && o.Account == Alice), "the re-quote must cancel the quote");
        Measure("OfferCreate, cancel and replace", iterations, () => OfferCreateCrossing.Cross(quoted, requote));
        Measure("OfferCreate, place without cancel", iterations, () => OfferCreateCrossing.Cross(quoted, quote));

        int chains = Math.Max(20, iterations / 10);
        Measure("15 tx, index rebuilt each step", chains, () => RunChain(snapshot, gateways, rebuildIndex: true));
        Measure("15 tx, rolled with SnapshotAfter", chains, () => RunChain(snapshot, gateways, rebuildIndex: false));
    }

    private static void Measure(string name, int iterations, Func<object> run)
    {
        for (int i = 0; i < Math.Max(20, iterations / 10); i++)
            GC.KeepAlive(run());

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();

        double[] samples = new double[iterations];
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            GC.KeepAlive(run());
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        Array.Sort(samples);
        double mean = 0;
        foreach (double sample in samples)
            mean += sample;
        mean /= iterations;

        Console.WriteLine(
            $"{name,-34} {mean,6:F3}ms {samples[iterations / 2],6:F3}ms {samples[(int)(iterations * 0.95)],6:F3}ms " +
            $"{allocated / (double)iterations / 1024.0,7:F0} KiB");
    }
}
