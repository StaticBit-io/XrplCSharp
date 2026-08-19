using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests
{
    /// <summary>
    /// Regression tests for <c>ChangeServer</c> pointed at a server that is not up yet.
    /// <para>
    /// <c>ChangeServer</c> used to set the global <c>_isIntentionalDisconnect</c> flag, which was only ever
    /// reset in <c>OnceOpen</c>. When the new server never came up, the flag stayed set, the failure of the
    /// new connection was read as a user disconnect ("Connection closed permanently."), no reconnect loop
    /// was started, and every later call failed with "No connection attempt in progress. Call Connect()
    /// first." - the client was dead even after the server came up.
    /// </para>
    /// </summary>
    [TestClass]
    public class TestUChangeServerFailure
    {
        private CreateMockRippled _mockedRippled;
        private CreateMockRippled _secondRippled;
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
                await _client.Disconnect();
                _client = null;
            }

            _mockedRippled?.Stop();
            _secondRippled?.Stop();
        }

        /// <summary>
        /// Switching to a server that is not listening yet must leave the client reconnecting, so it comes
        /// up on its own once that server appears - not stranded in a permanent disconnect.
        /// </summary>
        [TestMethod]
        public async Task TestChangeServerToUnreachableServerRecoversWhenItComesUp()
        {
            _client = new XrplClient($"ws://127.0.0.1:{_port}", new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(200),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
                MaxReconnectAttempts = 50,
                StopAfterMaxAttempts = false,
                // Short on purpose: ChangeServer gives up waiting quickly, but the reconnect loop it left
                // behind is what this test is about.
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(3),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                UseCustomPing = false,
            });

            await _client.Connect();
            Assert.IsTrue(_client.connection.IsConnected(), "Precondition: client must be connected to the first server.");

            int secondPort = TestUtils.GetFreePort(); // nothing is listening there yet

            try
            {
                await _client.connection.ChangeServer($"ws://127.0.0.1:{secondPort}");
            }
            catch (Exception)
            {
                // Expected - the target is not up yet. What matters is the state it leaves behind.
            }

            Assert.AreNotEqual(
                XrpConnectionState.Disconnected,
                _client.connection.CurrentConnectionState,
                "A ChangeServer target that is down is a connection failure, not a permanent disconnect.");

            // The server appears afterwards - exactly the "start the node later" case.
            // The mock binds on a background thread, so a port taken in the meantime would
            // surface as a 30s timeout below rather than as a bind error; check first.
            Assert.IsTrue(
                TestUtils.IsPortStillFree(secondPort),
                $"Port {secondPort} was taken by another process while the test held it — rerun.");
            _secondRippled = StartMock(secondPort);

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!_client.connection.IsConnected() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            Assert.IsTrue(
                _client.connection.IsConnected(),
                $"Client never reached the new server after it came up (state: {_client.connection.CurrentConnectionState}).");
            Assert.AreEqual($"ws://127.0.0.1:{secondPort}", _client.connection.GetUrl());

            Dictionary<string, object> response =
                await _client.Request(new Dictionary<string, object> { { "command", "server_info" } }).Typed();
            Assert.IsNotNull(response, "Client must be usable on the new server.");
        }

        /// <summary>
        /// The same, after an explicit user <c>Disconnect()</c>: the global intentional-disconnect flag left
        /// behind by it must not suppress reconnection for the server <c>ChangeServer</c> switches to.
        /// </summary>
        [TestMethod]
        public async Task TestChangeServerAfterUserDisconnectStillReconnects()
        {
            _client = new XrplClient($"ws://127.0.0.1:{_port}", new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(200),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
                MaxReconnectAttempts = 50,
                StopAfterMaxAttempts = false,
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(3),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                UseCustomPing = false,
            });

            await _client.Connect();
            await _client.Disconnect();

            int secondPort = TestUtils.GetFreePort();

            try
            {
                await _client.connection.ChangeServer($"ws://127.0.0.1:{secondPort}");
            }
            catch (Exception)
            {
                // Expected - the target is not up yet.
            }

            Assert.IsTrue(
                TestUtils.IsPortStillFree(secondPort),
                $"Port {secondPort} was taken by another process while the test held it — rerun.");
            _secondRippled = StartMock(secondPort);

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!_client.connection.IsConnected() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            Assert.IsTrue(
                _client.connection.IsConnected(),
                $"Client never reached the new server after a user disconnect (state: {_client.connection.CurrentConnectionState}).");
        }
    }
}
