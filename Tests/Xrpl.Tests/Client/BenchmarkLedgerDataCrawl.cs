using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using System.Text.Json;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Manual benchmark of a long paged crawl through the full client stack (socket receive loop,
    /// message routing, RequestManager, JSON round-trip). Deliberately named outside the
    /// TestU/TestI filters so it never runs in CI; invoke it explicitly:
    /// <c>dotnet test --filter "FullyQualifiedName~BenchmarkLedgerDataCrawl"</c>.
    /// Knobs: CRAWL_PAGES, CRAWL_PAYLOAD_BYTES, CRAWL_FRAGMENTS, CRAWL_RETAIN_PER_PAGE.
    /// CRAWL_RETAIN_PER_PAGE models a consumer that keeps every crawled object alive (a full
    /// ledger-state snapshot), which is what makes each forced gen2 collection progressively
    /// more expensive as the crawl advances.
    /// </summary>
    [TestClass]
    public class BenchmarkLedgerDataCrawl
    {
        private static int EnvInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out int value) && value >= 0 ? value : fallback;
        }

        /// <summary>Stand-in for one crawled ledger object the consumer keeps in its snapshot.</summary>
        private sealed class RetainedEntry
        {
            public RetainedEntry(string index)
            {
                Index = index;
            }

            public string Index { get; }
        }

        [TestMethod]
        public async Task BenchmarkSequentialPaging()
        {
            int pages = EnvInt("CRAWL_PAGES", 2000);
            int payloadBytes = EnvInt("CRAWL_PAYLOAD_BYTES", 2 * 1024 * 1024);
            int fragments = EnvInt("CRAWL_FRAGMENTS", 32);
            int retainPerPage = EnvInt("CRAWL_RETAIN_PER_PAGE", 0);

            List<RetainedEntry> snapshot = new List<RetainedEntry>(pages * retainPerPage);

            using PagedResponseServer server = new PagedResponseServer(payloadBytes, fragments);
            using XrplClient client = new XrplClient(server.Url);

            await client.Connect().ConfigureAwait(false);

            // One warm-up page so JIT and pooled buffers are not charged to the measured window.
            await RequestPageAsync(client).ConfigureAwait(false);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long heapBefore = GC.GetTotalMemory(false);
            long lohBefore = LohBytes();

            double[] pageMs = new double[pages];
            long startTicks = Stopwatch.GetTimestamp();

            for (int i = 0; i < pages; i++)
            {
                long before = Stopwatch.GetTimestamp();
                await RequestPageAsync(client).ConfigureAwait(false);

                for (int entry = 0; entry < retainPerPage; entry++)
                {
                    snapshot.Add(new RetainedEntry(((long)i * retainPerPage + entry).ToString("X16")));
                }

                pageMs[i] = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
            }

            double totalSeconds = (Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            long heapAfter = GC.GetTotalMemory(false);
            long lohAfter = LohBytes();

            await client.Disconnect().ConfigureAwait(false);

            Console.WriteLine("=== ledger_data crawl benchmark (full client stack) ===");
            Console.WriteLine($"pages             : {pages}");
            Console.WriteLine($"payload           : {payloadBytes / 1024.0 / 1024.0:F2} MiB");
            Console.WriteLine($"fragments per page: {fragments}");
            Console.WriteLine($"retained objects  : {snapshot.Count:N0} ({retainPerPage}/page)");
            Console.WriteLine($"total time        : {totalSeconds:F2} s ({pages / totalSeconds:F2} pages/s)");
            Console.WriteLine($"first decile avg  : {DecileAverage(pageMs, 0):F1} ms/page");
            Console.WriteLine($"last decile avg   : {DecileAverage(pageMs, 9):F1} ms/page");
            Console.WriteLine($"trend (last/first): {DecileAverage(pageMs, 9) / DecileAverage(pageMs, 0):F2}x");
            Console.WriteLine($"p50 / p99 / max   : {Percentile(pageMs, 50):F1} / {Percentile(pageMs, 99):F1} / " +
                              $"{Percentile(pageMs, 100):F1} ms");
            Console.WriteLine($"allocated total   : {allocated / 1024.0 / 1024.0 / 1024.0:F2} GiB");
            Console.WriteLine($"allocated per page: {allocated / (double)pages / 1024.0 / 1024.0:F1} MiB " +
                              $"({allocated / (double)pages / payloadBytes:F1}x payload)");
            Console.WriteLine($"gen0/gen1/gen2    : {GC.CollectionCount(0) - gen0Before} / " +
                              $"{GC.CollectionCount(1) - gen1Before} / {GC.CollectionCount(2) - gen2Before}");
            Console.WriteLine($"managed heap      : {heapBefore / 1024.0 / 1024.0:F1} -> {heapAfter / 1024.0 / 1024.0:F1} MiB");
            Console.WriteLine($"LOH size          : {lohBefore / 1024.0 / 1024.0:F1} -> {lohAfter / 1024.0 / 1024.0:F1} MiB");
            Console.WriteLine("decile profile (ms/page): " + string.Join(" | ", DecileProfile(pageMs)));
        }

        /// <summary>
        /// Same crawl through <c>GRequest&lt;JsonElement, …&gt;</c> — the path a consumer takes when
        /// it needs the raw ledger objects, because the typed <c>LOLedgerData.State</c> drops
        /// fields the models do not know. This is where the response path's own cost shows up
        /// undiluted: nothing is materialized into a model, so what is measured is receive,
        /// route and parse.
        /// </summary>
        [TestMethod]
        public async Task BenchmarkSequentialPagingUntyped()
        {
            int pages = EnvInt("CRAWL_PAGES", 2000);
            int payloadBytes = EnvInt("CRAWL_PAYLOAD_BYTES", 2 * 1024 * 1024);
            int fragments = EnvInt("CRAWL_FRAGMENTS", 32);

            using PagedResponseServer server = new PagedResponseServer(payloadBytes, fragments);
            using XrplClient client = new XrplClient(server.Url);

            await client.Connect().ConfigureAwait(false);
            await RequestUntypedPageAsync(client).ConfigureAwait(false);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            int gen2Before = GC.CollectionCount(2);
            long lohBefore = LohBytes();
            long startTicks = Stopwatch.GetTimestamp();

            for (int i = 0; i < pages; i++)
            {
                await RequestUntypedPageAsync(client).ConfigureAwait(false);
            }

            double totalSeconds = (Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            long lohAfter = LohBytes();

            await client.Disconnect().ConfigureAwait(false);

            Console.WriteLine("=== ledger_data crawl benchmark (untyped JsonElement result) ===");
            Console.WriteLine($"pages             : {pages}");
            Console.WriteLine($"payload           : {payloadBytes / 1024.0 / 1024.0:F2} MiB");
            Console.WriteLine($"total time        : {totalSeconds:F2} s ({pages / totalSeconds:F2} pages/s)");
            Console.WriteLine($"allocated per page: {allocated / (double)pages / 1024.0 / 1024.0:F1} MiB " +
                              $"({allocated / (double)pages / payloadBytes:F1}x payload)");
            Console.WriteLine($"gen2 collections  : {GC.CollectionCount(2) - gen2Before}");
            Console.WriteLine($"LOH size          : {lohBefore / 1024.0 / 1024.0:F1} -> {lohAfter / 1024.0 / 1024.0:F1} MiB");
        }

        private static async Task RequestUntypedPageAsync(XrplClient client)
        {
            JsonElement result = await client
                .GRequest<JsonElement, LedgerDataRequest>(new LedgerDataRequest
                {
                    LedgerIndex = new LedgerIndex(96000000),
                    Binary = true,
                    Limit = 2048
                })
                .Typed().ConfigureAwait(false);

            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("state", out JsonElement state))
            {
                throw new InvalidOperationException("empty ledger_data response");
            }

            if (state.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("ledger_data page carried no objects");
            }
        }

        private static async Task RequestPageAsync(XrplClient client)
        {
            Dictionary<string, object> request = new Dictionary<string, object>
            {
                ["command"] = "ledger_data",
                ["ledger_index"] = 96000000,
                ["binary"] = true,
                ["limit"] = 2048
            };

            Dictionary<string, object> response = await client.Request(request).Typed().ConfigureAwait(false);
            if (response == null)
            {
                throw new InvalidOperationException("empty ledger_data response");
            }
        }

        private static long LohBytes()
        {
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
            return generations.Length > 3 ? generations[3].SizeAfterBytes : 0;
        }

        private static double DecileAverage(double[] values, int decile)
        {
            int size = Math.Max(1, values.Length / 10);
            int from = decile * size;
            int to = Math.Min(values.Length, from + size);
            if (from >= to)
            {
                return 0;
            }

            double sum = 0;
            for (int i = from; i < to; i++)
            {
                sum += values[i];
            }

            return sum / (to - from);
        }

        private static string[] DecileProfile(double[] values)
        {
            string[] profile = new string[10];
            for (int i = 0; i < 10; i++)
            {
                profile[i] = DecileAverage(values, i).ToString("F1");
            }

            return profile;
        }

        private static double Percentile(double[] values, int percentile)
        {
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int index = (int)Math.Round((percentile / 100.0) * (sorted.Length - 1));
            return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }
    }
}
