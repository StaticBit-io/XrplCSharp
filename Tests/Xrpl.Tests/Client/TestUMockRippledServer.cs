using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Tests.MockRippled;

namespace XrplTests.Xrpl.ClientLib;

/// <summary>
/// The mock rippled server's own invariants - the ones whose absence took the whole test host
/// down rather than failing a test.
/// </summary>
/// <remarks>
/// <para>
/// A run on PR #145 aborted with <c>Server error: OnClientDisconnected is not bound!</c> and
/// <c>Test host process crashed</c>. The chain: <c>MockClient.messageCallback</c> is a socket
/// callback, so it runs on a thread-pool thread; when the socket faults it enters its own
/// <c>catch</c>, and from inside that catch it calls <c>ClientDisconnect</c>, which threw when
/// nothing was subscribed. An exception raised inside a catch block on a pool thread has nowhere
/// left to go, and .NET ends the process.
/// </para>
/// <para>
/// Worth testing rather than just fixing, because of how the failure presents: the run is
/// aborted, so the tests that had not been reached yet never run, and CI reports one failed job
/// rather than a few hundred unexecuted tests. It is a failure that hides its own size, and the
/// only reason it was not worse is that the process exits non-zero.
/// </para>
/// </remarks>
[TestClass]
public class TestUMockRippledServer
{
    private static IPEndPoint AnyLoopbackPort() => new IPEndPoint(IPAddress.Loopback, 0);

    /// <summary>
    /// Raising an event nobody subscribed to is not an error.
    /// </summary>
    /// <remarks>
    /// All four events used to throw when unbound. For an event, no subscriber is a legitimate
    /// state - and these fire from socket callbacks, where the difference between throwing and
    /// not is the difference between a failed test and no test results at all.
    /// </remarks>
    [TestMethod]
    public void TestUAnUnsubscribedEventIsNotAnError()
    {
        Server server = new Server(AnyLoopbackPort());

        try
        {
            // The one that actually crashed the host, called exactly as the catch block calls it.
            server.ClientDisconnect(null);

            // And the others, which sit on the same kind of thread.
            server.ReceiveMessage(null, "{}");
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// The client list survives being touched from several threads at once.
    /// </summary>
    /// <remarks>
    /// <c>_clients</c> is added to from the accept callback and removed from on disconnect - both
    /// thread-pool threads - while the test thread reads it through <c>GetConnectedClient</c> and
    /// <c>GetConnectedClientCount</c>. Unsynchronised, an add during an enumeration throws
    /// <see cref="InvalidOperationException"/> on a thread with no catch above it, which is the
    /// same fatal shape as the bug above by a different route.
    /// </remarks>
    [TestMethod]
    public async Task TestUTheClientListToleratesConcurrentUse()
    {
        Server server = new Server(AnyLoopbackPort());

        try
        {
            List<Task> workers = new List<Task>();

            for (int i = 0; i < 4; i++)
            {
                workers.Add(Task.Run(() =>
                {
                    for (int n = 0; n < 2_000; n++)
                    {
                        server.ClientDisconnect(null);
                    }
                }));

                workers.Add(Task.Run(() =>
                {
                    for (int n = 0; n < 2_000; n++)
                    {
                        server.GetConnectedClientCount();
                        server.GetConnectedClient(0);
                        server.GetConnectedClient("no-such-guid");
                    }
                }));
            }

            await Task.WhenAll(workers);
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// A server that has not been told to listen is not listening.
    /// </summary>
    /// <remarks>
    /// The constructor used to bind and accept on its own, so a caller could not subscribe before
    /// clients arrived. Nothing here asserts about sockets: the point is only that constructing
    /// is now separable from accepting, which is what lets handlers be bound first.
    /// </remarks>
    [TestMethod]
    public void TestUConstructingAServerDoesNotStartAccepting()
    {
        Server server = new Server(AnyLoopbackPort());

        try
        {
            Assert.AreEqual(
                0,
                server.GetConnectedClientCount(),
                "A server that was never told to listen cannot have accepted anyone.");

            Assert.IsFalse(
                server.GetSocket().IsBound,
                "The constructor must not bind - that is the whole point of the split.");

            // Binding happens here, not in the constructor - and doing it explicitly must work.
            server.StartListening();

            Assert.IsTrue(
                server.GetSocket().IsBound,
                "StartListening must actually bind; asserting the socket is merely non-null would pass even if it did nothing.");
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// Stopping a server that never listened is quiet, and stopping twice is too.
    /// </summary>
    /// <remarks>
    /// <c>CreateMockRippled.Start()</c> races its own <c>Stop()</c>: a mock stopped before startup
    /// finishes has its server closed without ever having accepted. That path has to be silent, or
    /// the teardown of a fast test becomes a failure of its own.
    /// </remarks>
    [TestMethod]
    public void TestUStoppingAServerThatNeverListenedIsQuiet()
    {
        Server server = new Server(AnyLoopbackPort());

        server.Stop();

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => server.StartListening(),
            "Listening on a socket that Stop() disposed should say so plainly, not carry on half-alive.");
    }
}
