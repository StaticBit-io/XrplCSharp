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
/// A frame from a socket that is being retired must not reach handlers as if it were current.
/// </summary>
/// <remarks>
/// Retirement is not instant: <c>RetireOldSessionAsync</c> runs fire-and-forget alongside the new
/// connection and closes the old socket gracefully, so that socket keeps delivering for as long as
/// the close handshake takes - with its message callback still attached. After a reconnect those
/// frames are merely stale. After a <c>ChangeServer</c> between networks they describe a different
/// chain: transactions for accounts that do not exist on the new one, ledger indexes from
/// somewhere else entirely.
/// <para>
/// The lifecycle callbacks have always compared their captured session against the active one;
/// the message path was the exception, carrying no session at all.
/// </para>
/// </remarks>
[TestClass]
public class TestUStaleSessionFrames
{
    private const string ScriptedReply =
        "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{}}";

    private const string TransactionMessage = """
    {
      "type": "transaction",
      "status": "closed",
      "validated": true,
      "engine_result": "tesSUCCESS",
      "tx_json": { "TransactionType": "Payment", "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd", "Sequence": 9 },
      "meta": { "AffectedNodes": [], "TransactionIndex": 0, "TransactionResult": "tesSUCCESS" }
    }
    """;

    /// <summary>
    /// A frame tagged with a session that is not the active one is dropped and counted.
    /// </summary>
    [TestMethod]
    public async Task TestUFrameFromARetiredSessionNeverReachesHandlers()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();

        int calls = 0;
        client.OnTransaction += _ =>
        {
            calls++;
            return Task.CompletedTask;
        };

        try
        {
            // long.MaxValue stands in for a session id that is no longer active - sessions are
            // numbered upward from the first connection, so this can never be the live one.
            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage),
                sessionId: long.MaxValue);

            Assert.AreEqual(1L, client.connection.StaleSessionFramesDropped,
                "a frame from a session that is not active must be dropped before it reaches the queue");
            Assert.AreEqual(0, calls, "the handler saw a frame belonging to a connection that is being retired");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    /// <summary>
    /// A frame carrying the id of the session that is being retired is dropped too.
    /// </summary>
    /// <remarks>
    /// This is the case a plain id comparison misses. <c>ChangeServer</c> and the reconnect loop
    /// mark the session retiring while it is still the active one - its replacement is installed
    /// later, by <c>ConnectInternalAsync</c> - so frames arriving in that window carry an id that
    /// matches. Whether the new connection is up yet decides nothing about whether these frames
    /// belong to it.
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameFromTheSessionBeingRetiredIsDropped()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();

        int calls = 0;
        client.OnTransaction += _ =>
        {
            calls++;
            return Task.CompletedTask;
        };

        try
        {
            long? retiringSessionId = client.connection.ActiveSessionId;
            Assert.IsNotNull(retiringSessionId, "a connected client must have a session to retire");
            client.connection.MarkActiveSessionRetiringForTests();

            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage),
                retiringSessionId);

            Assert.AreEqual(1L, client.connection.StaleSessionFramesDropped,
                "an id that matches a retiring session is not an id that matches the live one");
            Assert.AreEqual(0, calls, "the handler saw a frame from the session being retired");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    /// <summary>
    /// A frame that was queued while its session was live is still dropped if the session stops
    /// being live before the frame is dispatched.
    /// </summary>
    /// <remarks>
    /// Checking on the way into the queue cannot be the guarantee. The queue holds up to
    /// <c>StreamMessageQueueCapacity</c> frames (10 000 by default) and the channel itself is
    /// rebuilt per session under a different lock, so between the check and the handler call the
    /// session can retire and the client can be on another network entirely.
    /// <para>
    /// Made deterministic by stalling the reader: the processor is a single reader that awaits
    /// each handler, so a handler that does not return holds the second frame in the queue for as
    /// long as the test needs.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameQueuedBeforeRetirementIsNotDispatchedAfterIt()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();

        TaskCompletionSource firstFrameEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstFrame = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        client.OnTransaction += async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstFrameEntered.TrySetResult();
                await releaseFirstFrame.Task;
            }
        };

        try
        {
            long? sessionId = client.connection.ActiveSessionId;
            Assert.IsNotNull(sessionId, "a connected client must have a session");

            // Frame one occupies the reader and parks it inside the handler.
            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage), sessionId);

            Task entered = await Task.WhenAny(firstFrameEntered.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(firstFrameEntered.Task, entered, "the reader never reached the handler");

            // Frame two is accepted into the queue - the session is still live at this point - and
            // waits there because the reader is parked.
            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage), sessionId);
            Assert.AreEqual(0L, client.connection.StaleSessionFramesDropped,
                "both frames were queued while the session was live");

            // Only now does the session stop being the live one.
            client.connection.MarkActiveSessionRetiringForTests();
            releaseFirstFrame.TrySetResult();

            // The reader wakes, takes frame two and must refuse it.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (client.connection.StaleSessionFramesDropped == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.AreEqual(1L, client.connection.StaleSessionFramesDropped,
                "a frame dequeued after its session retired must not be dispatched");
            Assert.AreEqual(1, Volatile.Read(ref calls),
                "the handler saw a frame belonging to a session the client had already left");
        }
        finally
        {
            releaseFirstFrame.TrySetResult();
            await client.Disconnect();
        }
    }

    /// <summary>
    /// The guard must not reject frames from the live session, or the stream stops entirely.
    /// </summary>
    /// <remarks>
    /// Without this, a comparison that always failed would pass the test above while breaking
    /// every subscription in the SDK.
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameFromTheActiveSessionIsDelivered()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();

        TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnTransaction += r =>
        {
            received.TrySetResult(r);
            return Task.CompletedTask;
        };

        try
        {
            // No session named: the ordinary OnMessage entry point, which anyone may call and
            // which has no session to compare against. It must be delivered, not dropped.
            await client.connection.OnMessage(TransactionMessage);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "a frame with no session attached was dropped");

            Assert.AreEqual(0L, client.connection.StaleSessionFramesDropped,
                "nothing here came from a retired session");
        }
        finally
        {
            await client.Disconnect();
        }
    }
}
