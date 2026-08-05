using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests
{
    /// <summary>
    /// Concurrency smoke tests for the reconnect session — the <c>_reconnectCts</c> /
    /// <c>_reconnectLoop</c> / <c>_reconnectAttempts</c> triple, which is now updated under a
    /// shared lock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These do not reproduce the race the lock fixes.</b> That window is a few instructions
    /// wide — a start landing between the stop path's cancel, dispose and null — and driving it
    /// from public API calls, which are separated by whole awaits, does not hit it: with the lock
    /// removed again these tests still pass. Claiming them as regression coverage would be false.
    /// </para>
    /// <para>
    /// What they do earn their place for is the other direction. Introducing a lock around the
    /// session creates a deadlock risk of its own: the loop is now started while the lock is held,
    /// and anything that called back into consumer code from there could re-enter a path that takes
    /// the same lock. These tests hammer ChangeServer and Disconnect concurrently and require the
    /// client to still reach a live server afterwards, so a deadlock or a lost session shows up as
    /// a hang or a failure here rather than in production.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUReconnectSessionRaces
    {
        private CreateMockRippled _mockedRippled;
        private XrplClient _client;
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

        private static CreateMockRippled StartMock(int port)
        {
            CreateMockRippled mock = new CreateMockRippled(port) { suppressOutput = true };
            mock.AddResponse("server_info", ServerInfoResponse());

            Thread listenerThread = new Thread(() => mock.Start()) { IsBackground = true };
            listenerThread.Start();
            return mock;
        }

        [TestInitialize]
        public void MyTestInitialize()
        {
            _port = TestUtils.GetFreePort();
            _mockedRippled = StartMock(_port);
        }

        [TestCleanup]
        public async Task MyTestCleanup()
        {
            if (_client != null)
            {
                try
                {
                    await _client.Disconnect();
                }
                catch (Exception)
                {
                    // The client may already be down; cleanup must not mask the test result.
                }

                _client = null;
            }

            _mockedRippled?.Stop();
        }

        private XrplClient CreateClient(string url) =>
            new XrplClient(url, new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(50),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(400),
                MaxReconnectAttempts = 100,
                StopAfterMaxAttempts = false,
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(20),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                UseCustomPing = false,
            });

        /// <summary>
        /// Concurrent <c>ChangeServer</c> calls tear down and install reconnect sessions from several
        /// threads at once. Whatever interleaving wins, the client must end up able to connect to the
        /// live server — not stranded with a disposed or orphaned session.
        /// </summary>
        [TestMethod]
        public async Task TestConcurrentChangeServerKeepsClientRecoverable()
        {
            int deadPortA = TestUtils.GetFreePort();
            int deadPortB = TestUtils.GetFreePort();

            _client = CreateClient($"ws://127.0.0.1:{_port}");
            await _client.Connect();
            Assert.IsTrue(_client.connection.IsConnected(), "Precondition: connected to the live mock.");

            // Writers of the reconnect session running at once: two pointed at ports where nothing
            // listens (each starts a reconnect sequence), one pointed back at the live server, plus a
            // Disconnect taking the session down underneath them.
            for (int round = 0; round < 5; round++)
            {
                Task[] racers =
                {
                    SwitchTo($"ws://127.0.0.1:{deadPortA}"),
                    SwitchTo($"ws://127.0.0.1:{deadPortB}"),
                    SwitchTo($"ws://127.0.0.1:{_port}"),
                    Task.Run(async () =>
                    {
                        // Disconnect takes the same session down while the switches install new
                        // ones — the stop-vs-start interleaving the lock has to make safe.
                        try
                        {
                            await _client.Disconnect();
                        }
                        catch (Exception)
                        {
                        }
                    }),
                };

                await Task.WhenAll(racers);
            }

            // Whoever won, point the client at the live server and require it to get there.
            await SwitchTo($"ws://127.0.0.1:{_port}");
            try { await _client.Connect(); } catch (Exception) { }

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!_client.connection.IsConnected() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            Assert.IsTrue(
                _client.connection.IsConnected(),
                "After concurrent ChangeServer calls the client could not reach a server that is up — " +
                "the reconnect session was left disposed or orphaned.");
        }

        /// <summary>
        /// <c>Disconnect</c> racing a reconnect sequence must leave the client cleanly stopped and
        /// still able to reconnect afterwards — a stop that tore down someone else's session would
        /// either strand a live loop or leave a stale one running.
        /// </summary>
        [TestMethod]
        public async Task TestDisconnectRacingReconnectLeavesClientReconnectable()
        {
            int deadPort = TestUtils.GetFreePort();

            _client = CreateClient($"ws://127.0.0.1:{_port}");
            await _client.Connect();

            for (int round = 0; round < 5; round++)
            {
                // Start a reconnect sequence against a dead port and disconnect while it runs.
                Task switching = SwitchTo($"ws://127.0.0.1:{deadPort}");
                Task disconnecting = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                    await _client.Disconnect();
                });

                await Task.WhenAll(switching, disconnecting);
            }

            // The client must still be usable: point it back at the live server and connect.
            await SwitchTo($"ws://127.0.0.1:{_port}");
            await _client.Connect();

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!_client.connection.IsConnected() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            Assert.IsTrue(
                _client.connection.IsConnected(),
                "Disconnect racing a reconnect sequence left the client unable to connect again.");
        }

        private async Task SwitchTo(string url)
        {
            try
            {
                await _client.connection.ChangeServer(url);
            }
            catch (Exception)
            {
                // Failing to reach a dead port is the point of the race; the invariant is asserted
                // by the caller once the dust settles.
            }
        }
    }
}
