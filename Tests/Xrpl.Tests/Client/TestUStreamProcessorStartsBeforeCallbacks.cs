using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Subscriptions;
using Xrpl.Tests;

namespace XrplTests.Client;

/// <summary>
/// Frames answered while the <c>OnConnected</c> callback is still running go through the queue,
/// like every other frame.
/// </summary>
/// <remarks>
/// Subscribing from <c>OnConnected</c> is the ordinary pattern - it is what the wallet consuming
/// this SDK does - and the node can answer that subscription before the handler returns. The
/// message processor used to start at the very end of <c>OnceOpen</c>, after the callback, so
/// those first frames found no channel and took the fallback: outside
/// <c>StreamMessageQueueCapacity</c>, uncounted by <c>DroppedStreamMessages</c>, and dispatched
/// concurrently rather than one at a time. The events most likely to arrive out of order were
/// precisely the first ones after connecting.
/// <para>
/// Moving the start earlier was blocked by an unrelated coupling: <c>StartPingTimer</c> begins with
/// <c>StopPingTimerSync</c>, which stopped the message processor as well, so an earlier start was
/// torn down again moments later. The two lifecycles are now separate.
/// </para>
/// </remarks>
[TestClass]
public class TestUStreamProcessorStartsBeforeCallbacks
{
    private const string ScriptedReply =
        "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{}}";

    private const string TransactionMessage = """
    {
      "type": "transaction",
      "status": "closed",
      "validated": true,
      "engine_result": "tesSUCCESS",
      "tx_json": { "TransactionType": "Payment", "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd", "Sequence": 4 },
      "meta": { "AffectedNodes": [], "TransactionIndex": 0, "TransactionResult": "tesSUCCESS" }
    }
    """;

    /// <summary>
    /// A frame driven from inside the <c>OnConnected</c> handler reaches the queue, not the
    /// fallback.
    /// </summary>
    /// <remarks>
    /// <c>FallbackDispatchedStreamMessages</c> is the whole assertion: it counts frames dispatched
    /// outside the queue, so before this change it would have counted this one.
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameAnsweredDuringOnConnectedGoesThroughTheQueue()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);

        bool channelExisted = false;
        long fallbackDuringCallback = -1;
        TaskCompletionSource callbackDone = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.connection.OnConnected += async () =>
        {
            // Stands in for the node answering a subscription issued from this very handler.
            channelExisted = client.connection.IsMessageProcessorRunning;
            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage),
                client.connection.ActiveSessionId);
            fallbackDuringCallback = client.connection.FallbackDispatchedStreamMessages;
            callbackDone.TrySetResult();
        };

        await client.Connect();

        try
        {
            // Connect() returning is not enough: OnceOpen resolves the waiters before invoking
            // this callback, so without the wait the assertions below can read their defaults.
            Task finished = await Task.WhenAny(callbackDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(callbackDone.Task, finished, "the OnConnected handler never ran");

            Assert.IsTrue(channelExisted,
                "the queue did not exist while consumer code was subscribing - frames answered there take the fallback");
            Assert.AreEqual(0L, fallbackDuringCallback,
                "a frame answered during OnConnected went round the queue");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    /// <summary>
    /// The queue survives the ping timer starting, which happens after the callback.
    /// </summary>
    /// <remarks>
    /// This is the coupling that blocked the fix: <c>StartPingTimer</c> calls
    /// <c>StopPingTimerSync</c>, and that used to stop the message processor too. Starting the
    /// processor early is worth nothing if the ping timer tears it down a moment later, and the
    /// symptom is invisible - the fallback keeps delivering frames, just without any of the queue's
    /// guarantees.
    /// </remarks>
    [TestMethod]
    public async Task TestUStartingThePingTimerLeavesTheProcessorRunning()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(
            server.Url,
            new XrplClient.ClientOptions { UseCustomPing = true });
        await client.Connect();

        try
        {
            // The ping timer starts at the very end of OnceOpen, after Connect() has returned.
            // Asserting before it runs would pass whether or not it takes the processor down -
            // the test has to wait for the thing that used to break it.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!client.connection.IsPingTimerRunning && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.IsTrue(client.connection.IsPingTimerRunning, "the ping timer never started");

            Assert.IsTrue(client.connection.IsMessageProcessorRunning,
                "the ping timer took the message processor down with it");

            long fallbackBefore = client.connection.FallbackDispatchedStreamMessages;

            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            client.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage),
                client.connection.ActiveSessionId);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "the frame never reached the handler");

            Assert.AreEqual(0L, client.connection.FallbackDispatchedStreamMessages - fallbackBefore,
                "the frame took the fallback, so the processor was not running after all");
        }
        finally
        {
            await client.Disconnect();
        }
    }
}
