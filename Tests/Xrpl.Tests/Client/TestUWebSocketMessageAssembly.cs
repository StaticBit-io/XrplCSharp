using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Regression coverage for assembling a WebSocket message that arrives as several receive
    /// chunks. The assembly buffer is reused for the life of the connection, so the tests check
    /// both that every message comes out byte-exact and that the cost per message does not grow
    /// with the number of chunks it was split into.
    /// </summary>
    // GC.GetTotalAllocatedBytes is process-wide, so the allocation assertion below would pick up
    // whatever other test classes allocate alongside it.
    [TestClass]
    [DoNotParallelize]
    public class TestUWebSocketMessageAssembly
    {
        private const int WaitSeconds = 120;

        [TestMethod]
        public async Task TestUMultiChunkMessageArrivesIntact()
        {
            // Deliberately larger than the client's 1 MiB receive buffer and split far more finely
            // than the buffer would split it on its own.
            const int PayloadBytes = 3 * 1024 * 1024;

            using BulkMessageServer server = new BulkMessageServer(4, PayloadBytes, fragments: 96);
            IReadOnlyList<string> messages = await ReceiveAsync(server, 4).ConfigureAwait(false);

            Assert.AreEqual(4, messages.Count);
            foreach (string message in messages)
            {
                Assert.AreEqual(server.PayloadText, message);
            }
        }

        [TestMethod]
        public async Task TestUShortMessageAfterLongOneIsNotPaddedWithStaleBytes()
        {
            const int PayloadBytes = 2 * 1024 * 1024;
            int[] lengthCycle = { PayloadBytes, PayloadBytes / 8, PayloadBytes / 2, 1024 };

            using BulkMessageServer server = new BulkMessageServer(
                messageCount: 12,
                payloadBytes: PayloadBytes,
                fragments: 16,
                lengthCycle: lengthCycle);

            IReadOnlyList<string> messages = await ReceiveAsync(server, 12).ConfigureAwait(false);

            Assert.AreEqual(12, messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                int expectedLength = lengthCycle[i % lengthCycle.Length];
                Assert.AreEqual(expectedLength, messages[i].Length, $"message {i} has the wrong length");
                Assert.AreEqual(server.PayloadText.Substring(0, expectedLength), messages[i],
                    $"message {i} does not match the expected prefix");
            }
        }

        /// <summary>
        /// Guards the shape of the fix: assembly used to be quadratic in the number of chunks, so
        /// allocation per message grew with the split. The bound is deliberately loose — the point
        /// is that a 64-way split must not cost an order of magnitude more than the payload.
        /// </summary>
        [TestMethod]
        public async Task TestUAssemblyAllocationDoesNotGrowWithChunkCount()
        {
            const int MessageCount = 200;
            const int PayloadBytes = 1024 * 1024;

            // Floor per message is the exact-sized byte[] plus the UTF-16 string handed to the
            // callback, i.e. about 3x the payload. Quadratic assembly at 64 chunks cost ~34x.
            const double AllowedTimesPayload = 12.0;

            using BulkMessageServer server = new BulkMessageServer(MessageCount, PayloadBytes, fragments: 64);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            IReadOnlyList<string> messages = await ReceiveAsync(server, MessageCount).ConfigureAwait(false);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            Assert.AreEqual(MessageCount, messages.Count);

            double perMessage = allocated / (double)MessageCount / PayloadBytes;
            Assert.IsTrue(
                perMessage < AllowedTimesPayload,
                $"allocated {perMessage:F1}x payload per message, expected below {AllowedTimesPayload:F1}x");
        }

        private static async Task<IReadOnlyList<string>> ReceiveAsync(BulkMessageServer server, int expected)
        {
            List<string> messages = new List<string>(expected);
            TaskCompletionSource<bool> done =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            WebSocketClient client = WebSocketClient.Create(server.Url);
            client.OnMessageReceived((message, _) =>
            {
                messages.Add(message);
                if (messages.Count >= expected)
                {
                    done.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

            try
            {
                await client.Connect().ConfigureAwait(false);

                // The server holds off until the client speaks, so nothing is missed.
                client.SendMessage("go");

                Task completed = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(WaitSeconds)))
                    .ConfigureAwait(false);
                Assert.AreSame(
                    done.Task,
                    completed,
                    $"only {messages.Count} of {expected} messages arrived; server fault: {server.Fault?.ToString() ?? "none"}");
            }
            finally
            {
                client.CancelIntentionally();
                client.Dispose();
            }

            return messages;
        }
    }
}
