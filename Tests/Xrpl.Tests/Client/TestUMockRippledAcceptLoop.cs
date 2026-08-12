using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests
{
    /// <summary>
    /// Guards the mock server's accept loop against a single bad connection taking it down.
    /// </summary>
    /// <remarks>
    /// The mock re-arms <c>BeginAccept</c> as the last statement of its accept callback, so any
    /// throw earlier in that callback — a peer that resets the connection before or during the
    /// WebSocket handshake — used to end the loop for good. The listen socket stayed bound, so the
    /// port still looked taken and connects still completed at the TCP level, but nothing was ever
    /// accepted again: every later client hung until its own connect timeout.
    ///
    /// That is not a hypothetical. It is what made <see cref="TestUReconnectSessionRaces"/> flaky
    /// on CI: concurrent ChangeServer/Disconnect calls abort half-open connections, one of those
    /// aborts silenced the mock, and the assertion at the end of the test then blamed the client
    /// for not reaching "a server that is up" — while the server was in fact deaf.
    /// </remarks>
    [TestClass]
    public class TestUMockRippledAcceptLoop
    {
        private CreateMockRippled _mock;
        private int _port;

        private static Dictionary<string, object> ServerInfoResponse() => new Dictionary<string, object>
        {
            { "type", "response" },
            { "status", "success" },
            { "result", new Dictionary<string, object>
                {
                    { "info", new Dictionary<string, object>
                        {
                            { "build_version", "test-mock" },
                            { "complete_ledgers", "1-1" },
                            { "server_state", "full" },
                        }
                    },
                }
            },
        };

        [TestInitialize]
        public void MyTestInitialize()
        {
            _port = TestUtils.GetFreePort();
            _mock = new CreateMockRippled(_port) { suppressOutput = true };
            _mock.AddResponse("server_info", ServerInfoResponse());
            _mock.Start();
        }

        [TestCleanup]
        public void MyTestCleanup() => _mock?.Stop();

        /// <summary>
        /// Resets a connection while the mock is in its accept callback, then requires the mock to
        /// still serve the next client.
        /// </summary>
        [TestMethod]
        public async Task TestAbortedHandshakeLeavesTheMockAccepting()
        {
            // A zero linger time makes Close() send RST rather than FIN, so the mock's blocking
            // Receive of the handshake fails with "connection reset by peer" — the exact throw
            // seen in the CI log of the flaky run.
            using (Socket rude = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                rude.LingerState = new LingerOption(true, 0);
                await rude.ConnectAsync(IPAddress.Loopback, _port);
                rude.Close();
            }

            // Give the mock a moment to run its callback and (before the fix) fall out of it.
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            XrplClient client = new XrplClient($"ws://127.0.0.1:{_port}", new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(5),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                UseCustomPing = false,
            });

            try
            {
                await client.Connect();

                Assert.IsTrue(
                    client.connection.IsConnected(),
                    "The mock stopped accepting after one aborted connection — its accept loop was not re-armed.");
            }
            finally
            {
                try
                {
                    await client.Disconnect();
                }
                catch (Exception)
                {
                    // Cleanup must not mask the assertion above.
                }
            }
        }

        /// <summary>
        /// The handshake probe the reconnect tests use to attribute a failure must actually tell
        /// a serving mock from a deaf one — a probe that always says "alive" would be worse than
        /// none, since it would confirm the wrong suspect.
        /// </summary>
        [TestMethod]
        public void TestHandshakeProbeTellsAServingMockFromADeafOne()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(2);

            Assert.IsTrue(
                TestUtils.MockCompletesHandshake(_port, timeout),
                "A running mock must answer the probe with a 101 upgrade.");

            Assert.IsFalse(
                TestUtils.MockCompletesHandshake(TestUtils.GetFreePort(), timeout),
                "Nothing listens on that port, so the probe must report it as not serving.");

            // The case the probe exists for: a socket that is bound and listening but never
            // accepts. The TCP connect still succeeds — which is why a plain connect check proves
            // nothing — and only the missing handshake reveals that the server is deaf.
            TcpListener deaf = new TcpListener(IPAddress.Loopback, 0);
            deaf.Start();
            try
            {
                int deafPort = ((IPEndPoint)deaf.LocalEndpoint).Port;
                Assert.IsFalse(
                    TestUtils.MockCompletesHandshake(deafPort, timeout),
                    "A listening socket that never accepts must be reported as not serving.");
            }
            finally
            {
                deaf.Stop();
            }
        }

        /// <summary>
        /// The same guarantee under repetition: a run of aborted connections must not degrade the
        /// mock, since the reconnect tests abort several in a row.
        /// </summary>
        [TestMethod]
        public async Task TestRepeatedAbortedHandshakesLeaveTheMockAccepting()
        {
            for (int i = 0; i < 10; i++)
            {
                using Socket rude = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                rude.LingerState = new LingerOption(true, 0);
                await rude.ConnectAsync(IPAddress.Loopback, _port);
                rude.Close();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));

            XrplClient client = new XrplClient($"ws://127.0.0.1:{_port}", new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(5),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                UseCustomPing = false,
            });

            try
            {
                await client.Connect();

                Assert.IsTrue(
                    client.connection.IsConnected(),
                    "The mock stopped accepting after a run of aborted connections.");
            }
            finally
            {
                try
                {
                    await client.Disconnect();
                }
                catch (Exception)
                {
                    // Cleanup must not mask the assertion above.
                }
            }
        }
    }
}
