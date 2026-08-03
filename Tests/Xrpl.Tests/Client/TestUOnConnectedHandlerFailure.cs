using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;

namespace Xrpl.Tests
{
    /// <summary>
    /// Regression tests for the "silent wedge": an exception thrown by a consumer <c>OnConnected</c>
    /// handler used to trigger the user-disconnect path (<c>_permanentlyDisconnected = true</c>),
    /// which killed the client forever instead of reconnecting.
    /// </summary>
    [TestClass]
    public class TestUOnConnectedHandlerFailure
    {
        private CreateMockRippled _mockedRippled;
        private XrplClient _client;
        private int _port;

        [TestInitialize]
        public void MyTestInitialize()
        {
            _port = TestUtils.GetFreePort();
            _mockedRippled = new CreateMockRippled(_port) { suppressOutput = true };
            _mockedRippled.AddResponse("server_info", new Dictionary<string, object>
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
            });

            Thread tcpListenerThread = new Thread(() => _mockedRippled.Start()) { IsBackground = true };
            tcpListenerThread.Start();
        }

        [TestCleanup]
        public async Task MyTestCleanup()
        {
            if (_client != null)
            {
                await _client.Disconnect();
                _client = null;
            }
        }

        private XrplClient CreateClient(int maxReconnectAttempts, bool stopAfterMaxAttempts) =>
            new XrplClient($"ws://127.0.0.1:{_port}", new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
                MaxReconnectAttempts = maxReconnectAttempts,
                StopAfterMaxAttempts = stopAfterMaxAttempts,
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(20),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(10),
                UseCustomPing = false,
            });

        /// <summary>
        /// A transient failure inside <c>OnConnected</c> (e.g. a subscribe that timed out because the
        /// node accepts TCP before it serves requests) must not strand the client: the socket is torn
        /// down and the regular reconnect loop must bring it back.
        /// </summary>
        [TestMethod]
        public async Task TestTransientOnConnectedFailureRecovers()
        {
            int invocations = 0;
            TaskCompletionSource<bool> reconnected =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _client = CreateClient(maxReconnectAttempts: 10, stopAfterMaxAttempts: false);
            _client.connection.OnConnected += () =>
            {
                if (Interlocked.Increment(ref invocations) == 1)
                {
                    throw new InvalidOperationException("subscribe failed after connect");
                }

                reconnected.TrySetResult(true);
                return Task.CompletedTask;
            };

            Exception connectError = null;
            try
            {
                await _client.Connect();
            }
            catch (Exception error)
            {
                connectError = error;
            }

            Task completed = await Task.WhenAny(reconnected.Task, Task.Delay(TimeSpan.FromSeconds(30)));

            Assert.AreSame(
                reconnected.Task,
                completed,
                $"Client never reconnected after OnConnected threw (invocations: {Volatile.Read(ref invocations)}, connect error: {connectError?.Message ?? "none"})");
            Assert.IsTrue(_client.connection.IsConnected(), "Client must be connected again after recovery.");
        }

        /// <summary>
        /// After recovery the client must still be usable — the permanent-disconnect flag must not be set.
        /// </summary>
        [TestMethod]
        public async Task TestClientIsUsableAfterOnConnectedFailure()
        {
            int invocations = 0;
            TaskCompletionSource<bool> reconnected =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _client = CreateClient(maxReconnectAttempts: 10, stopAfterMaxAttempts: false);
            _client.connection.OnConnected += () =>
            {
                if (Interlocked.Increment(ref invocations) == 1)
                {
                    throw new InvalidOperationException("subscribe failed after connect");
                }

                reconnected.TrySetResult(true);
                return Task.CompletedTask;
            };

            try
            {
                await _client.Connect();
            }
            catch (Exception)
            {
                // Recovery is asserted below - the initial Connect() may observe the failed attempt.
            }

            Task completed = await Task.WhenAny(reconnected.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.AreSame(reconnected.Task, completed, "Client never reconnected after OnConnected threw.");

            Dictionary<string, object> request = new Dictionary<string, object>
            {
                { "command", "server_info" },
            };

            Dictionary<string, object> response = await _client.Request(request);
            Assert.IsNotNull(response, "Request after recovery must succeed.");
        }

        /// <summary>
        /// A permanently broken handler must not spin forever: with <c>StopAfterMaxAttempts</c> the client
        /// gives up after <c>MaxReconnectAttempts</c> consecutive handler failures.
        /// </summary>
        [TestMethod]
        public async Task TestPermanentlyFailingOnConnectedHandlerStops()
        {
            const int maxAttempts = 3;
            int invocations = 0;

            _client = CreateClient(maxReconnectAttempts: maxAttempts, stopAfterMaxAttempts: true);
            _client.connection.OnConnected += () =>
            {
                Interlocked.Increment(ref invocations);
                throw new InvalidOperationException("handler is permanently broken");
            };

            Exception connectError = null;
            try
            {
                await _client.Connect();
            }
            catch (Exception error)
            {
                connectError = error;
            }

            Assert.IsInstanceOfType<NotConnectedException>(
                connectError,
                $"Giving up must unblock the waiting caller with NotConnectedException, got: {connectError?.GetType().Name ?? "no exception"}.");

            await Task.Delay(TimeSpan.FromSeconds(10));
            int settled = Volatile.Read(ref invocations);
            await Task.Delay(TimeSpan.FromSeconds(5));

            Assert.AreEqual(
                settled,
                Volatile.Read(ref invocations),
                "Client kept retrying a permanently failing OnConnected handler instead of giving up.");
            Assert.IsTrue(
                settled <= maxAttempts + 1,
                $"Handler was retried {settled} times, expected at most {maxAttempts + 1}.");
            Assert.IsFalse(_client.connection.IsConnected(), "Client must not report a live connection.");
        }

        /// <summary>
        /// The reason the connection was torn down must be observable through <c>OnError</c>.
        /// </summary>
        [TestMethod]
        public async Task TestOnConnectedFailureIsReportedThroughOnError()
        {
            int invocations = 0;
            TaskCompletionSource<string> reported =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            _client = CreateClient(maxReconnectAttempts: 10, stopAfterMaxAttempts: false);
            _client.connection.OnError += (error, errorMessage, message, data) =>
            {
                if (errorMessage == "connectHandlerError")
                {
                    reported.TrySetResult(message);
                }

                return Task.CompletedTask;
            };
            _client.connection.OnConnected += () =>
            {
                if (Interlocked.Increment(ref invocations) == 1)
                {
                    throw new InvalidOperationException("subscribe failed after connect");
                }

                return Task.CompletedTask;
            };

            try
            {
                await _client.Connect();
            }
            catch (Exception)
            {
                // The failure itself is asserted through OnError below.
            }

            Task completed = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.AreSame(completed, reported.Task, "OnConnected failure was never reported through OnError.");
            StringAssert.Contains(reported.Task.Result, "subscribe failed after connect");
        }
    }
}
