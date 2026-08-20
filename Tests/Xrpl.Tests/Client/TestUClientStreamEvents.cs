using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Subscriptions;

namespace XrplTests.Client;

/// <summary>
/// Stream events are reachable through <see cref="IXrplClient"/>, not only through the concrete
/// <c>connection</c> field.
/// </summary>
/// <remarks>
/// This is what makes the raw bytes on a stream event usable through the SDK's own contract: a
/// wallet renders <c>transaction</c> events for signing, and until now the only way to receive one
/// was <c>client.connection.OnTransaction</c> - a property of a concrete class, so code written
/// against the interface could neither subscribe nor be tested against a substitute client.
/// </remarks>
[TestClass]
public class TestUClientStreamEvents
{
    private const string TransactionMessage = """
    {
      "type": "transaction",
      "status": "closed",
      "validated": true,
      "engine_result": "tesSUCCESS",
      "tx_json": {
        "TransactionType": "Payment",
        "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
        "Sequence": 13
      },
      "meta": { "AffectedNodes": [], "TransactionIndex": 0, "TransactionResult": "tesSUCCESS" }
    }
    """;

    /// <summary>
    /// Waits until the counter reaches <paramref name="expected"/>, or fails.
    /// </summary>
    /// <remarks>
    /// <c>OnMessage</c> returning does not mean handlers have run: a stream frame is handed to the
    /// background processor, and when there is none - an unconnected client, as here - to a task
    /// that yields before doing anything, so that parsing and handler code never run on the
    /// receive loop. Both paths are asynchronous, which is the point; the test has to wait rather
    /// than assume.
    /// </remarks>
    private static void WaitForCount(Func<int> counter, int expected, string message)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (counter() < expected && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.AreEqual(expected, counter(), message);
    }

    /// <summary>
    /// A handler registered through the interface receives the event, and the raw bytes come with
    /// it - the whole point of reaching the stream through the contract.
    /// </summary>
    [TestMethod]
    public async Task TestUSubscribingThroughTheInterfaceReceivesStreamEvents()
    {
        using XrplClient client = new XrplClient("wss://localhost:1/");
        IXrplClient contract = client;

        TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        contract.OnTransaction += r =>
        {
            received.TrySetResult(r);
            return Task.CompletedTask;
        };

        await client.connection.OnMessage(TransactionMessage);

        Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.AreSame(received.Task, completed, "the handler registered through IXrplClient was never invoked");

        TransactionStream result = await received.Task;
        Assert.AreEqual(13u, result.Transaction.Sequence);
        Assert.IsFalse(result.Raw.IsEmpty, "the event carries the bytes the node sent, which is why reaching it through the contract matters");
    }

    /// <summary>
    /// Subscribing through the client and through its connection reach one list, because the
    /// client forwards rather than relaying.
    /// </summary>
    /// <remarks>
    /// Pins the forwarding shape itself: a relaying implementation would keep its own subscriber
    /// list, and removing through one surface would leave the other still subscribed.
    /// </remarks>
    [TestMethod]
    public async Task TestUHandlerAddedThroughTheClientCanBeRemovedThroughTheConnection()
    {
        using XrplClient client = new XrplClient("wss://localhost:1/");
        IXrplClient contract = client;

        int calls = 0;
        OnTransaction handler = _ =>
        {
            calls++;
            return Task.CompletedTask;
        };

        // Subscribed through the client, removed through the connection. Only forwarding makes
        // that work: a relaying client would keep its own subscriber list, the removal would miss
        // it, and the handler would keep firing. Removing through the same surface it was added
        // to cannot tell the two apart - both pass - which is why the test crosses surfaces.
        // A witness that is never removed, added after the handler so it runs after it: it tells
        // the test when a frame has been dispatched. Without it, "calls is still 1" after the
        // removal would pass just as well for a frame that has not been processed yet.
        int dispatched = 0;
        client.connection.OnTransaction += _ =>
        {
            Interlocked.Increment(ref dispatched);
            return Task.CompletedTask;
        };

        contract.OnTransaction += handler;
        await client.connection.OnMessage(TransactionMessage);
        WaitForCount(() => Volatile.Read(ref dispatched), 1, "the first frame was never dispatched");
        Assert.AreEqual(1, calls, "sanity: the handler is attached");

        client.connection.OnTransaction -= handler;
        await client.connection.OnMessage(TransactionMessage);
        WaitForCount(() => Volatile.Read(ref dispatched), 2, "the second frame was never dispatched");

        Assert.AreEqual(1, calls, "removing through the connection left the handler attached - the client is relaying into its own list rather than forwarding");
    }

    /// <summary>
    /// The mirror case: added through the connection, removed through the client.
    /// </summary>
    /// <remarks>
    /// Needed as its own test because the one above never executes the client's <c>remove</c>
    /// accessor - it removes through the connection. Verified by mutation: turning the client's
    /// <c>remove</c> into <c>connection.OnTransaction += value</c> left the whole suite green
    /// until this existed.
    /// </remarks>
    [TestMethod]
    public async Task TestUHandlerAddedThroughTheConnectionCanBeRemovedThroughTheClient()
    {
        using XrplClient client = new XrplClient("wss://localhost:1/");
        IXrplClient contract = client;

        int calls = 0;
        OnTransaction handler = _ =>
        {
            calls++;
            return Task.CompletedTask;
        };

        client.connection.OnTransaction += handler;

        int dispatched = 0;
        client.connection.OnTransaction += _ =>
        {
            Interlocked.Increment(ref dispatched);
            return Task.CompletedTask;
        };

        await client.connection.OnMessage(TransactionMessage);
        WaitForCount(() => Volatile.Read(ref dispatched), 1, "the first frame was never dispatched");
        Assert.AreEqual(1, calls, "sanity: the handler is attached");

        contract.OnTransaction -= handler;
        await client.connection.OnMessage(TransactionMessage);
        WaitForCount(() => Volatile.Read(ref dispatched), 2, "the second frame was never dispatched");

        Assert.AreEqual(1, calls, "removing through the client left the handler attached - its remove accessor does not reach the connection");
    }

    /// <summary>
    /// <c>connection</c> is read-only on the contract, so a caller cannot swap the object every
    /// handler is attached to and leave the stream silently unreachable.
    /// </summary>
    [TestMethod]
    public void TestUConnectionCannotBeReplacedThroughTheContract()
    {
        System.Reflection.PropertyInfo property = typeof(IXrplClient).GetProperty(nameof(IXrplClient.connection));

        Assert.IsNotNull(property);
        Assert.IsNull(property.SetMethod, "a settable connection would let a caller strand every handler registered through these events on the old object");
    }
}
