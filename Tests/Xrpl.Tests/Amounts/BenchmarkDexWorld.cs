using System;
using System.Diagnostics;

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
            ReserveBase = snapshot.ReserveBase,
            ReserveIncrement = snapshot.ReserveIncrement,
            Accounts = snapshot.Accounts,
            TrustLines = snapshot.TrustLines,
            Offers = snapshot.Offers,
        };

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
