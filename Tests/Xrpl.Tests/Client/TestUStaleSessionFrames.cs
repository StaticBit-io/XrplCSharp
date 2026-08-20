using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Concurrent;
using System.Linq;
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
    /// The same message with a sequence number a test can recognise.
    /// </summary>
    private static string TransactionMessageWith(uint sequence) =>
        TransactionMessage.Replace("\"Sequence\": 9", $"\"Sequence\": {sequence}");

    private const uint DroppedSequence = 9;

    private const uint WitnessSequence = 77;

    /// <summary>
    /// Waits until the handler has seen the witness frame, then reports whether it also saw the
    /// frame that was supposed to be dropped.
    /// </summary>
    /// <remarks>
    /// A bare "the handler was not called" assertion cannot fail: dispatch is asynchronous on both
    /// paths, so an undropped frame would simply not have arrived yet when the assertion ran. The
    /// witness fixes that. It carries no session - <c>OnMessage</c>, which is never rejected - and
    /// is submitted after the frame under test, so the single reader would have dispatched that
    /// one first had it been queued at all. Seeing the witness therefore means the other one is
    /// never coming.
    /// </remarks>
    private static async Task DriveWitnessAndWait(XrplClient client, ConcurrentQueue<uint> seen)
    {
        await client.connection.OnMessage(TransactionMessageWith(WitnessSequence));

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!seen.Contains(WitnessSequence) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(seen.Contains(WitnessSequence),
            "the witness frame never arrived, so nothing here can be concluded about the other one");
    }

    /// <summary>
    /// Waits until stream frames are queued rather than taking the fallback path.
    /// </summary>
    /// <remarks>
    /// <c>Connect()</c> returning is not enough: <c>OnceOpen</c> resolves the waiters first and
    /// starts the message processor last, so a test that injects a frame the moment it connects
    /// can find no queue to put it in.
    /// </remarks>
    private static async Task WaitForMessageProcessor(XrplClient client)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!client.connection.IsMessageProcessorRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(client.connection.IsMessageProcessorRunning,
            "the message processor never started");
    }

    /// <summary>
    /// A frame tagged with a session that is not the active one is dropped and counted.
    /// </summary>
    [TestMethod]
    public async Task TestUFrameFromARetiredSessionNeverReachesHandlers()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();
        await WaitForMessageProcessor(client);

        ConcurrentQueue<uint> seen = new ConcurrentQueue<uint>();
        client.OnTransaction += r =>
        {
            seen.Enqueue(r.Transaction.Sequence ?? 0);
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

            await DriveWitnessAndWait(client, seen);
            Assert.IsFalse(seen.Contains(DroppedSequence),
                "the handler saw a frame belonging to a connection that is being retired");
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
        await WaitForMessageProcessor(client);

        ConcurrentQueue<uint> seen = new ConcurrentQueue<uint>();
        client.OnTransaction += r =>
        {
            seen.Enqueue(r.Transaction.Sequence ?? 0);
            return Task.CompletedTask;
        };

        try
        {
            // Marked and named in one lock: reading the id separately would let a reconnect swap
            // the session in between, and the test would quietly fall back to the mismatch case.
            long? retiringSessionId = client.connection.MarkActiveSessionRetiringForTests();
            Assert.IsNotNull(retiringSessionId, "a connected client must have a session to retire");

            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage),
                retiringSessionId);

            Assert.AreEqual(1L, client.connection.StaleSessionFramesDropped,
                "an id that matches a retiring session is not an id that matches the live one");

            await DriveWitnessAndWait(client, seen);
            Assert.IsFalse(seen.Contains(DroppedSequence),
                "the handler saw a frame from the session being retired");
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
        await WaitForMessageProcessor(client);

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
    /// A frame the channel refuses is still delivered, through the fallback path.
    /// </summary>
    /// <remarks>
    /// The bounded channel uses <c>DropOldest</c>, so a full queue is not a refusal - it evicts and
    /// reports success. <c>TryWrite</c> returns <see langword="false"/> only for a completed
    /// writer, which <c>StopMessageProcessorInternal</c> produces after clearing the field: anyone
    /// holding the reference from an instant earlier writes into a closed channel. Since
    /// <c>StartPingTimer</c> stops the processor and <c>StartMessageProcessor</c> rebuilds it on
    /// every connect, this is the ordinary path rather than a corner of it, and a frame lost here
    /// would be lost silently.
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameRefusedByACompletedChannelTakesTheFallbackPath()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();
        await WaitForMessageProcessor(client);

        TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnTransaction += r =>
        {
            received.TrySetResult(r);
            return Task.CompletedTask;
        };

        try
        {
            long? sessionId = client.connection.ActiveSessionId;
            Assert.IsNotNull(sessionId, "a connected client must have a session");

            client.connection.CompleteStreamChannelWriterForTests();

            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage), sessionId);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed,
                "a frame the channel refused was neither queued nor dispatched - it vanished");

            Assert.AreEqual(0L, client.connection.StaleSessionFramesDropped,
                "the session was live throughout; nothing here is stale");

            Assert.AreEqual(1L, client.connection.FallbackDispatchedStreamMessages,
                "a frame that went round the queue must say so - that is what the counter is for");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    /// <summary>
    /// The fallback path hands the frame off before doing any of the work, so the receive loop
    /// does not synchronously parse JSON or run handlers.
    /// </summary>
    /// <remarks>
    /// An async method runs on its caller's thread up to the first real await, and the first real
    /// await inside <c>ProcessStreamMessageAsync</c> comes after <c>JsonSerializer.Deserialize</c>.
    /// Without a yield at the top of the fallback, everything up to a handler's own first await
    /// runs inline on the socket callback - the head-of-line blocking the queue exists to prevent,
    /// reintroduced for the startup window, a stopped processor and a refused write.
    /// <para>
    /// Deterministic by construction: the handler parks on a blocking wait. If the frame were
    /// processed inline, the injecting call could not return until the handler was released, so
    /// the wait below would time out rather than merely be slow.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TestUFallbackPathReturnsBeforeHandlersRun()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();
        await WaitForMessageProcessor(client);

        using ManualResetEventSlim handlerEntered = new ManualResetEventSlim(initialState: false);
        using ManualResetEventSlim releaseHandler = new ManualResetEventSlim(initialState: false);

        client.OnTransaction += _ =>
        {
            handlerEntered.Set();
            releaseHandler.Wait(TimeSpan.FromSeconds(10));
            return Task.CompletedTask;
        };

        try
        {
            long? sessionId = client.connection.ActiveSessionId;
            Assert.IsNotNull(sessionId, "a connected client must have a session");

            // Completing the writer forces the next frame onto the fallback path.
            client.connection.CompleteStreamChannelWriterForTests();

            // Injected from a task of its own: were the frame processed inline, the call itself
            // would block and there would be no Task to wait on.
            Task inject = Task.Run(() => client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage), sessionId));

            Task finished = await Task.WhenAny(inject, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(inject, finished,
                "the fallback ran the frame inline: injection did not return while the handler was parked");

            Assert.IsTrue(handlerEntered.Wait(TimeSpan.FromSeconds(5)),
                "the frame never reached the handler at all");
        }
        finally
        {
            releaseHandler.Set();
            await client.Disconnect();
        }
    }

    /// <summary>
    /// A frame naming the live session is delivered.
    /// </summary>
    /// <remarks>
    /// The sessionless case below cannot stand in for this one: a guard that rejected every named
    /// session would pass it and still take the whole stream down, since every frame the socket
    /// produces is named.
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameNamingTheLiveSessionIsDelivered()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(ScriptedReply);
        using XrplClient client = new XrplClient(server.Url);
        await client.Connect();
        await WaitForMessageProcessor(client);

        TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnTransaction += r =>
        {
            received.TrySetResult(r);
            return Task.CompletedTask;
        };

        try
        {
            long? sessionId = client.connection.ActiveSessionId;
            Assert.IsNotNull(sessionId, "a connected client must have a session");

            await client.connection.IOnMessageFastPath(
                Encoding.UTF8.GetBytes(TransactionMessage), sessionId);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed,
                "a frame from the session the client is actually on was dropped");

            Assert.AreEqual(0L, client.connection.StaleSessionFramesDropped,
                "nothing here came from a session the client had left");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    /// <summary>
    /// A frame that names no session is delivered.
    /// </summary>
    /// <remarks>
    /// <c>OnMessage</c> is public and has no session to name, so there is nothing to compare it
    /// against. Rejecting it for want of a session would break the one entry point a caller can
    /// drive by hand.
    /// </remarks>
    [TestMethod]
    public async Task TestUFrameNamingNoSessionIsDelivered()
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
