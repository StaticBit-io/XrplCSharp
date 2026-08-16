using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Covers how <see cref="RequestManager"/> turns a response into the requested type. The
    /// response arrives already parsed, so the <c>result</c> member is deserialized straight from
    /// its <see cref="JsonElement"/>: rendering it back to text and parsing it a second time used
    /// to cost two extra copies of the whole response per request, both large-object-heap sized on
    /// a paged crawl. These tests pin the behaviour that must survive that, and the allocation
    /// budget that must not creep back up.
    /// </summary>
    [TestClass]
    public class TestUResponseParsing
    {
        private static string BuildLedgerDataMessage(Guid id, int entries)
        {
            StringBuilder builder = new StringBuilder(entries * 128 + 256);
            builder.Append("{\"id\":\"").Append(id.ToString("D"))
                   .Append("\",\"status\":\"success\",\"type\":\"response\",\"result\":{")
                   .Append("\"ledger_hash\":\"842B57C1CC0613299A686D3E9F310EC0422C84D3911E5056389AA7E5808A93C8\",")
                   .Append("\"ledger_index\":96000000,\"validated\":true,\"marker\":\"AABBCCDD\",\"state\":[");

            for (int i = 0; i < entries; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"LedgerEntryType\":\"AccountRoot\",\"Account\":\"rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH\",")
                       .Append("\"Balance\":\"").Append(1000000 + i)
                       .Append("\",\"Flags\":0,\"OwnerCount\":").Append(i % 17)
                       .Append(",\"Sequence\":").Append(i + 1)
                       .Append(",\"index\":\"").Append(i.ToString("X64")).Append("\"}");
            }

            builder.Append("]}}");
            return builder.ToString();
        }

        /// <summary>Rewrites the 36-character id of a prebuilt message in place.</summary>
        private static void WriteId(byte[] message, int offset, Guid id)
        {
            string text = id.ToString("D");
            for (int i = 0; i < text.Length; i++)
            {
                message[offset + i] = (byte)text[i];
            }
        }

        private static RequestManager.XrplGRequest Pending<T>(RequestManager manager)
        {
            return manager.CreateGRequest<T, LedgerDataRequest>(
                new LedgerDataRequest { Limit = 4 },
                System.Threading.Timeout.InfiniteTimeSpan);
        }

        [TestMethod]
        public void TestUntypedRequestGetsTheParsedResultNode()
        {
            RequestManager manager = new RequestManager();
            RequestManager.XrplGRequest pending = Pending<JsonElement>(manager);

            manager.HandleResponse(BuildLedgerDataMessage(pending.Id, 4));

            JsonElement result = (JsonElement)pending.Promise.GetAwaiter().GetResult();
            Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
            Assert.AreEqual(4, result.GetProperty("state").GetArrayLength());
            Assert.AreEqual(96000000, result.GetProperty("ledger_index").GetInt32());
            Assert.AreEqual("AABBCCDD", result.GetProperty("marker").GetString());

            // The element must outlive the parse: it is handed out rather than copied, so it has
            // to own its data and stay readable after everything else is collected.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            Assert.AreEqual(4, result.GetProperty("state").GetArrayLength());
        }

        [TestMethod]
        public void TestTypedRequestDeserializesFromTheParsedResultNode()
        {
            RequestManager manager = new RequestManager();
            RequestManager.XrplGRequest pending = Pending<LOLedgerData>(manager);

            manager.HandleResponse(BuildLedgerDataMessage(pending.Id, 3));

            LOLedgerData result = (LOLedgerData)pending.Promise.GetAwaiter().GetResult();
            Assert.IsNotNull(result);
            Assert.AreEqual(96000000u, result.LedgerIndex);
            Assert.AreEqual("842B57C1CC0613299A686D3E9F310EC0422C84D3911E5056389AA7E5808A93C8", result.LedgerHash);
            Assert.IsNotNull(result.State);
            Assert.AreEqual(3, result.State.Count);
        }

        [TestMethod]
        public void TestUtf8AndStringOverloadsProduceTheSameResult()
        {
            RequestManager manager = new RequestManager();

            RequestManager.XrplGRequest viaString = Pending<LOLedgerData>(manager);
            string message = BuildLedgerDataMessage(viaString.Id, 5);
            manager.HandleResponse(message);

            RequestManager.XrplGRequest viaBytes = Pending<LOLedgerData>(manager);
            manager.HandleResponse(Encoding.UTF8.GetBytes(BuildLedgerDataMessage(viaBytes.Id, 5)));

            LOLedgerData fromString = (LOLedgerData)viaString.Promise.GetAwaiter().GetResult();
            LOLedgerData fromBytes = (LOLedgerData)viaBytes.Promise.GetAwaiter().GetResult();

            Assert.AreEqual(fromString.LedgerIndex, fromBytes.LedgerIndex);
            Assert.AreEqual(fromString.LedgerHash, fromBytes.LedgerHash);
            Assert.AreEqual(fromString.State.Count, fromBytes.State.Count);
        }

        [TestMethod]
        public void TestResponseWithoutResultStillCompletes()
        {
            RequestManager manager = new RequestManager();

            RequestManager.XrplGRequest untyped = Pending<JsonElement>(manager);
            manager.HandleResponse($"{{\"id\":\"{untyped.Id:D}\",\"status\":\"success\",\"type\":\"response\",\"result\":null}}");
            JsonElement empty = (JsonElement)untyped.Promise.GetAwaiter().GetResult();
            Assert.AreEqual(JsonValueKind.Object, empty.ValueKind);
            Assert.IsFalse(empty.TryGetProperty("state", out _));

            RequestManager.XrplGRequest typed = Pending<LOLedgerData>(manager);
            manager.HandleResponse($"{{\"id\":\"{typed.Id:D}\",\"status\":\"success\",\"type\":\"response\"}}");
            LOLedgerData defaults = (LOLedgerData)typed.Promise.GetAwaiter().GetResult();
            Assert.IsNotNull(defaults);
            Assert.IsNull(defaults.State);
        }

        [TestMethod]
        public void TestErrorStatusRejectsWithTheParsedErrorResponse()
        {
            RequestManager manager = new RequestManager();
            RequestManager.XrplGRequest pending = Pending<LOLedgerData>(manager);

            manager.HandleResponse(
                $"{{\"id\":\"{pending.Id:D}\",\"status\":\"error\",\"type\":\"response\"," +
                "\"error\":\"lgrNotFound\",\"error_message\":\"ledgerNotFound\"}");

            RippledException rippled = null;
            try
            {
                pending.Promise.Wait();
            }
            catch (AggregateException raised)
            {
                rippled = raised.InnerException as RippledException;
            }

            Assert.IsNotNull(rippled, "the request should have been rejected with a RippledException");
            StringAssert.Contains(rippled.Message, "lgrNotFound");
            Assert.IsNotNull(rippled.Response, "the parsed error response must be attached");
            Assert.AreEqual("lgrNotFound", rippled.Response.Error);
            Assert.AreEqual("ledgerNotFound", rippled.Response.ErrorMessage);
        }

        /// <summary>
        /// Guards the allocation budget of the response path. Before the result node was
        /// deserialized directly, one response cost about 7.4 times its own byte length: a UTF-16
        /// copy of the message, a document over it, a second UTF-16 copy of the result, and a
        /// second document over that. The direct path costs about half of it from a string and
        /// about 1.7 times from UTF-8 bytes. The bound below sits between the two, well clear of
        /// both, so it fails only if the double round-trip comes back.
        /// </summary>
        [TestMethod]
        public void TestResponseParsingStaysWithinItsAllocationBudget()
        {
            const int Entries = 4096;
            const int Rounds = 12;

            RequestManager manager = new RequestManager();

            // Built once and reused, with only the id rewritten in place, so nothing the harness
            // allocates lands inside the measured window.
            RequestManager.XrplGRequest warmup = Pending<JsonElement>(manager);
            byte[] message = Encoding.UTF8.GetBytes(BuildLedgerDataMessage(warmup.Id, Entries));
            const int IdOffset = 7; // past {"id":"
            manager.HandleResponse(message);
            _ = warmup.Promise.GetAwaiter().GetResult();

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < Rounds; i++)
            {
                RequestManager.XrplGRequest pending = Pending<JsonElement>(manager);
                WriteId(message, IdOffset, pending.Id);
                manager.HandleResponse(message);
                JsonElement result = (JsonElement)pending.Promise.GetAwaiter().GetResult();
                Assert.AreEqual(Entries, result.GetProperty("state").GetArrayLength());
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double perResponse = allocated / (double)Rounds;
            double ratio = perResponse / message.Length;

            Console.WriteLine($"response {message.Length:N0} bytes, {perResponse / 1024 / 1024:F2} MB allocated per response ({ratio:F2}x)");

            Assert.IsTrue(
                ratio < 4.0,
                $"response parsing allocated {ratio:F2}x the response size, budget is 4x " +
                "(the pre-fix double round-trip cost about 7x here)");
        }

        /// <summary>
        /// The same budget one level up, over a real socket, because the budget above cannot see
        /// which overload <see cref="Connection"/> chooses. Binding the string callback again
        /// would put a UTF-16 copy of every frame back on the path and nothing else in the suite
        /// would notice.
        /// </summary>
        /// <remarks>
        /// Allocations here happen on the receive loop's thread, so this has to read the
        /// process-wide counter, which is why the test is kept out of the parallel pass.
        /// </remarks>
        [TestMethod]
        [DoNotParallelize]
        public async Task TestSocketPathKeepsResponsesInTheirWireForm()
        {
            const int Pages = 20;
            const int PayloadBytes = 1024 * 1024;

            using PagedResponseServer server = new PagedResponseServer(PayloadBytes, fragments: 8);
            using XrplClient client = new XrplClient(server.Url);

            await client.Connect().ConfigureAwait(false);
            await CrawlPageAsync(client).ConfigureAwait(false);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            long before = GC.GetTotalAllocatedBytes(precise: true);

            for (int i = 0; i < Pages; i++)
            {
                await CrawlPageAsync(client).ConfigureAwait(false);
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            await client.Disconnect().ConfigureAwait(false);

            double ratio = allocated / (double)Pages / PayloadBytes;
            Console.WriteLine($"socket path: {allocated / (double)Pages / 1024 / 1024:F2} MB allocated per page ({ratio:F2}x)");

            Assert.IsTrue(
                ratio < 3.0,
                $"the socket path allocated {ratio:F2}x the payload per page, budget is 3x " +
                "(2.18x as bound, 4.84x with the string callback bound instead)");
        }

        /// <summary>
        /// A response carrying <c>warning</c>/<c>warnings</c> still reaches both callbacks. The
        /// text they are handed is now built only when one of them is subscribed, so this is the
        /// side of that condition that must not have been broken.
        /// </summary>
        [TestMethod]
        public async Task TestWarningsStillReachTheirCallbacks()
        {
            using PagedResponseServer server = new PagedResponseServer(64 * 1024, fragments: 1, withWarnings: true);
            using XrplClient client = new XrplClient(server.Url);

            TaskCompletionSource<string> warning = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<int> serverWarnings = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await client.Connect().ConfigureAwait(false);

            client.connection.OnWarning += (text, message) =>
            {
                warning.TrySetResult(text);
                return Task.CompletedTask;
            };

            client.connection.OnServerWarning += (warnings, message) =>
            {
                serverWarnings.TrySetResult(warnings.Count);
                return Task.CompletedTask;
            };

            await CrawlPageAsync(client).ConfigureAwait(false);

            Task both = Task.WhenAll(warning.Task, serverWarnings.Task);
            Task finished = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            await client.Disconnect().ConfigureAwait(false);

            Assert.AreSame(both, finished, "a warned response did not reach OnWarning/OnServerWarning");
            Assert.AreEqual("load", await warning.Task.ConfigureAwait(false));
            Assert.AreEqual(1, await serverWarnings.Task.ConfigureAwait(false));
        }

        /// <summary>
        /// And the other side of it: warnings on every page with nothing subscribed must not put
        /// the UTF-16 copy of each response back on the path.
        /// </summary>
        /// <remarks>Reads the process-wide counter, so it stays out of the parallel pass.</remarks>
        [TestMethod]
        [DoNotParallelize]
        public async Task TestUnsubscribedWarningsCostNothing()
        {
            const int Pages = 20;
            const int PayloadBytes = 1024 * 1024;

            using PagedResponseServer server = new PagedResponseServer(PayloadBytes, fragments: 8, withWarnings: true);
            using XrplClient client = new XrplClient(server.Url);

            await client.Connect().ConfigureAwait(false);
            await CrawlPageAsync(client).ConfigureAwait(false);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            long before = GC.GetTotalAllocatedBytes(precise: true);

            for (int i = 0; i < Pages; i++)
            {
                await CrawlPageAsync(client).ConfigureAwait(false);
            }

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            await client.Disconnect().ConfigureAwait(false);

            double ratio = allocated / (double)Pages / PayloadBytes;
            Console.WriteLine($"warned pages, no subscribers: {allocated / (double)Pages / 1024 / 1024:F2} MB per page ({ratio:F2}x)");

            Assert.IsTrue(
                ratio < 3.0,
                $"warned responses allocated {ratio:F2}x the payload per page with nothing subscribed, " +
                "budget is 3x (2.08x as bound, 4.28x when the text is built regardless of subscribers)");
        }

        /// <summary>
        /// The string entry point is public and used by tests and consumers that feed messages in
        /// by hand. A null there travelled down to the stream processor and came back out through
        /// <c>OnError</c> as a <c>badMessage</c>; carrying the frame as bytes must not turn that
        /// into a throw out of the method itself, and must not turn it into silence either.
        /// </summary>
        [TestMethod]
        public async Task TestNullMessageIsStillReportedThroughOnError()
        {
            Connection connection = new Connection("ws://127.0.0.1:1/");
            TaskCompletionSource<string> reported = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            connection.OnError += (error, errorMessage, message, data) =>
            {
                reported.TrySetResult(errorMessage);
                return Task.CompletedTask;
            };

            // Must not throw: the entry point is public and a null used to be routed, not raised.
            await connection.OnMessage(null).ConfigureAwait(false);

            // The routing itself is fire-and-forget, so the report arrives after the call returns.
            Task completed = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);

            Assert.AreSame(reported.Task, completed, "a null message was dropped instead of being reported");
            Assert.AreEqual("badMessage", await reported.Task.ConfigureAwait(false));
        }

        private static async Task CrawlPageAsync(XrplClient client)
        {
            JsonElement page = await client
                .GRequest<JsonElement, LedgerDataRequest>(new LedgerDataRequest { Binary = true, Limit = 2048 })
                .ConfigureAwait(false);

            if (page.GetProperty("state").GetArrayLength() == 0)
            {
                throw new InvalidOperationException("ledger_data page carried no objects");
            }
        }
    }
}
