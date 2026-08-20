using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Subscriptions;
using Xrpl.Tests;

namespace XrplTests.Client;

/// <summary>
/// Stream messages discarded because handlers fell behind are counted, not lost in silence.
/// </summary>
/// <remarks>
/// The queue feeding stream handlers is bounded and drops its oldest entry when full, so a slow
/// handler costs events rather than stalling the socket. That trade is right, but it used to leave
/// no trace at all: nothing threw, nothing logged, and a consumer building state from the stream
/// drifted from the ledger with no way to tell. <c>DroppedStreamMessages</c> is that trace.
/// </remarks>
[TestClass]
public class TestUStreamMessageDropVisibility
{
    private const string TransactionMessage = """
    {
      "type": "transaction",
      "status": "closed",
      "validated": true,
      "engine_result": "tesSUCCESS",
      "tx_json": { "TransactionType": "Payment", "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd", "Sequence": 1 },
      "meta": { "AffectedNodes": [], "TransactionIndex": 0, "TransactionResult": "tesSUCCESS" }
    }
    """;

    /// <summary>
    /// A handler that never returns backs the queue up; past its capacity the oldest messages are
    /// discarded and counted.
    /// </summary>
    /// <remarks>
    /// Capacity is set to 2 through <see cref="Connection.ConnectionOptions.StreamMessageQueueCapacity"/>
    /// so the case is reached deterministically instead of by pushing ten thousand messages and
    /// hoping. The blocked handler is the realistic shape of the problem: a consumer doing
    /// something slow per event.
    /// </remarks>
    [TestMethod]
    public async Task TestUDroppedStreamMessagesCountsWhatTheConsumerNeverSaw()
    {
        // Connected on purpose: the bounded queue only exists once StartMessageProcessor has run,
        // which happens on a successful connect. Without it EnqueueStreamMessage falls back to
        // dispatching each message directly, there is no queue to overflow, and this test passes
        // while proving nothing - which is exactly what it did at first.
        using ScriptedResponseServer server = new ScriptedResponseServer(
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{}}");
        using XrplClient client = new XrplClient(
            server.Url,
            new XrplClient.ClientOptions { StreamMessageQueueCapacity = 2 });
        await client.Connect();

        TaskCompletionSource blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.OnTransaction += async _ =>
        {
            firstArrived.TrySetResult();
            await blocked.Task;
        };

        try
        {
            Assert.AreEqual(0L, client.DroppedStreamMessages, "nothing has been dropped before the queue is exercised");

            // The first message occupies the handler; everything after it queues behind.
            await client.connection.OnMessage(TransactionMessage);
            await firstArrived.Task;

            for (int i = 0; i < 12; i++)
            {
                await client.connection.OnMessage(TransactionMessage);
            }

            // Exact, not just non-zero: the reader took the first frame and is stuck on it, so the
            // twelve that follow meet a queue of capacity two - ten of them evict. A >0 assertion
            // would also pass if the callback missed some.
            Assert.AreEqual(
                10L,
                client.DroppedStreamMessages,
                $"twelve writes into a capacity-two queue behind a blocked handler should evict exactly ten, found {client.DroppedStreamMessages}");
        }
        finally
        {
            // Released here, not after the assertion: a failed assertion would otherwise leave the
            // handler parked on an unfinished task and the client connected.
            blocked.TrySetResult();
            await client.Disconnect();
        }
    }

    /// <summary>
    /// A consumer that keeps up loses nothing, so the counter stays at zero.
    /// </summary>
    /// <remarks>
    /// Without this, a counter wired to increment unconditionally would pass the test above.
    /// </remarks>
    [TestMethod]
    public async Task TestUDroppedStreamMessagesStaysZeroWhenTheConsumerKeepsUp()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{}}");
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();

        const int Sent = 20;

        int seen = 0;
        TaskCompletionSource allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnTransaction += _ =>
        {
            if (Interlocked.Increment(ref seen) == Sent)
            {
                allArrived.TrySetResult();
            }

            return Task.CompletedTask;
        };

        try
        {
            for (int i = 0; i < Sent; i++)
            {
                await client.connection.OnMessage(TransactionMessage);
            }

            // Awaited, not asserted straight away: delivery is asynchronous now that messages
            // travel through the queue, so reading the counter immediately after sending measures
            // nothing.
            Task completed = await Task.WhenAny(allArrived.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(allArrived.Task, completed, $"only {Volatile.Read(ref seen)} of {Sent} messages reached the handler");

            Assert.AreEqual(0L, client.DroppedStreamMessages, "the handler returned immediately every time - nothing should have been discarded");
        }
        finally
        {
            await client.Disconnect();
        }
    }
}
