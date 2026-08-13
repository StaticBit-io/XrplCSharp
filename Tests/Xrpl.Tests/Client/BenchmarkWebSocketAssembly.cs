using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Manual benchmark of the WebSocket multi-chunk message assembly path. Deliberately named
    /// outside the TestU/TestI filters so it never runs in CI; invoke it explicitly:
    /// <c>dotnet test --filter "FullyQualifiedName~BenchmarkWebSocketAssembly"</c>.
    /// Knobs: BENCH_MESSAGES, BENCH_PAYLOAD_BYTES, BENCH_FRAGMENTS.
    /// </summary>
    [TestClass]
    public class BenchmarkWebSocketAssembly
    {
        private static int EnvInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out int value) && value > 0 ? value : fallback;
        }

        [TestMethod]
        public async Task BenchmarkMultiChunkAssembly()
        {
            int messageCount = EnvInt("BENCH_MESSAGES", 600);
            int payloadBytes = EnvInt("BENCH_PAYLOAD_BYTES", 2 * 1024 * 1024);
            int fragments = EnvInt("BENCH_FRAGMENTS", 32);

            using BulkMessageServer server = new BulkMessageServer(messageCount, payloadBytes, fragments);

            long[] timestamps = new long[messageCount];
            int received = 0;
            int corrupted = 0;
            TaskCompletionSource<bool> allReceived =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            WebSocketClient client = WebSocketClient.Create(server.Url);
            client.OnMessageReceived((message, _) =>
            {
                int index = received;
                if (message.Length != payloadBytes)
                {
                    Interlocked.Increment(ref corrupted);
                }

                if (index < timestamps.Length)
                {
                    timestamps[index] = Stopwatch.GetTimestamp();
                }

                received = index + 1;
                if (received >= messageCount)
                {
                    allReceived.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

            // Warm up the JIT and the socket path before the measured window.
            await client.Connect().ConfigureAwait(false);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long lohBefore = LohBytes();
            long startTicks = Stopwatch.GetTimestamp();

            // Releases the server; nothing has been sent before this point.
            client.SendMessage("go");

            Task completed = await Task.WhenAny(
                allReceived.Task,
                Task.Delay(TimeSpan.FromMinutes(30))).ConfigureAwait(false);

            long stopTicks = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            long lohAfter = LohBytes();

            client.CancelIntentionally();
            client.Dispose();

            Assert.AreSame(allReceived.Task, completed, "benchmark did not finish within 30 minutes");
            Assert.AreEqual(0, corrupted, "at least one message was assembled with the wrong length");

            double totalSeconds = (stopTicks - startTicks) / (double)Stopwatch.Frequency;
            long allocated = allocatedAfter - allocatedBefore;

            double firstDecile = DecileAverageMs(timestamps, startTicks, 0);
            double lastDecile = DecileAverageMs(timestamps, startTicks, 9);
            double slowest = SlowestMs(timestamps, startTicks);

            Console.WriteLine("=== WebSocket multi-chunk assembly benchmark ===");
            Console.WriteLine($"messages          : {messageCount}");
            Console.WriteLine($"payload           : {payloadBytes / 1024.0 / 1024.0:F2} MiB");
            Console.WriteLine($"fragments per msg : {fragments} ({payloadBytes / fragments / 1024} KiB each)");
            Console.WriteLine($"total time        : {totalSeconds:F2} s ({messageCount / totalSeconds:F2} msg/s)");
            Console.WriteLine($"first decile avg  : {firstDecile:F2} ms/msg");
            Console.WriteLine($"last decile avg   : {lastDecile:F2} ms/msg");
            Console.WriteLine($"slowest message   : {slowest:F2} ms");
            Console.WriteLine($"trend (last/first): {lastDecile / firstDecile:F2}x");
            Console.WriteLine($"allocated total   : {allocated / 1024.0 / 1024.0:F1} MiB");
            Console.WriteLine($"allocated per msg : {allocated / (double)messageCount / 1024.0 / 1024.0:F2} MiB " +
                              $"({allocated / (double)messageCount / payloadBytes:F2}x payload)");
            Console.WriteLine($"gen0/gen1/gen2    : {GC.CollectionCount(0) - gen0Before} / " +
                              $"{GC.CollectionCount(1) - gen1Before} / {GC.CollectionCount(2) - gen2Before}");
            Console.WriteLine($"LOH size          : {lohBefore / 1024.0 / 1024.0:F1} MiB -> {lohAfter / 1024.0 / 1024.0:F1} MiB");
        }

        private static long LohBytes()
        {
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
            return generations.Length > 3 ? generations[3].SizeAfterBytes : 0;
        }

        private static double DecileAverageMs(long[] timestamps, long startTicks, int decile)
        {
            int size = Math.Max(1, timestamps.Length / 10);
            int from = decile * size;
            int to = Math.Min(timestamps.Length, from + size);
            if (from >= to)
            {
                return 0;
            }

            long previous = from == 0 ? startTicks : timestamps[from - 1];
            double totalMs = (timestamps[to - 1] - previous) * 1000.0 / Stopwatch.Frequency;
            return totalMs / (to - from);
        }

        private static double SlowestMs(long[] timestamps, long startTicks)
        {
            double slowest = 0;
            long previous = startTicks;

            foreach (long timestamp in timestamps)
            {
                double ms = (timestamp - previous) * 1000.0 / Stopwatch.Frequency;
                if (ms > slowest)
                {
                    slowest = ms;
                }

                previous = timestamp;
            }

            return slowest;
        }
    }
}
