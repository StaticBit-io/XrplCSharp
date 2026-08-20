using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;
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
