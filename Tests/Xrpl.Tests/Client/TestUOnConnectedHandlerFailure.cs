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
            _mockedRippled.AddResponse("server_info", ServerInfoResult());

            Thread tcpListenerThread = new Thread(() => _mockedRippled.Start()) { IsBackground = true };
            tcpListenerThread.Start();
        }

        private static Dictionary<string, object> ServerInfoResult() => new Dictionary<string, object>
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

        [TestCleanup]
        public async Task MyTestCleanup()
        {
            if (_client != null)
            {
                await _client.Disconnect();
                _client = null;
            }

            _mockedRippled?.Stop();
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
        /// A handler failure the client recovers from must not reach the caller at all - not even
        /// as a different exception. The connect succeeded; saying otherwise is simply wrong.
        /// </summary>
        /// <remarks>
        /// The reconnect path exists for exactly this: a handler that fails once and works on the
        /// next attempt. But the teardown in between rejects whatever the caller had in flight, and
        /// the caller is inside <c>SetNetworkId</c> by then - so <c>Connect()</c> threw
        /// <c>OperationCanceledException</c> while the client went on to connect. Measured before
        /// the fix: the caller got a cancellation and <c>IsConnected()</c> was <c>true</c> three
        /// seconds later.
        /// <para>
        /// Asking "is it connected now?" in that catch would not have worked either: at that moment
        /// the socket has just been torn down and the recovery has not finished, so the honest
        /// answer is no. The wait is what makes the difference between a connection being rebuilt
        /// and a client that gave up.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestRecoveredHandlerFailureWithRequestInFlightStillConnects()
        {
            _mockedRippled.AddDelayedResponse("server_info", ServerInfoResult(), TimeSpan.FromSeconds(2));

            _client = CreateClient(maxReconnectAttempts: 5, stopAfterMaxAttempts: true);

            int calls = 0;
            _client.OnConnected += async () =>
            {
                int call = Interlocked.Increment(ref calls);
                await Task.Delay(TimeSpan.FromMilliseconds(400));

                if (call == 1)
                {
                    throw new InvalidOperationException("first attempt fails, the next one works");
                }
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

            Assert.IsNull(
                connectError,
                $"The client recovered and connected, so Connect() must not report a failure. " +
                $"Got: {connectError}");
            Assert.IsTrue(
                _client.connection.IsConnected(),
                "Precondition of the assertion above: the client really did end up connected.");
            Assert.IsTrue(
                Volatile.Read(ref calls) >= 2,
                "The scenario requires the handler to have failed once and then run again.");
        }

        /// <summary>
        /// Giving up must reach the caller as <see cref="NotConnectedException"/> even when the
        /// caller had a request in flight at the moment the client gave up - issue #122.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Connect()</c> is two operations, not one: the connection itself, and the
        /// <c>server_info</c> that <c>SetNetworkId</c> sends straight after it. The socket really
        /// does open for a moment before the failing handler brings it down, so the wait can return
        /// successfully and the caller can already be inside that second operation when the give-up
        /// path tears everything down. Whatever rejects the in-flight request then decides what the
        /// caller sees - and none of the candidates is the right answer:
        /// <c>OperationCanceledException</c> says the caller cancelled something they never
        /// cancelled, <c>DisconnectedException</c> says a working connection went away.
        /// </para>
        /// <para>
        /// The delayed answer is what makes this deterministic. Answered at once, the window is
        /// reachable only by luck: the assertion failed on CI about every other run and never once
        /// in 37 local runs, which is why the issue sat open with the mechanism unproven. Holding
        /// <c>server_info</c> back puts the request in flight for certain.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestGivingUpWithARequestInFlightStillReportsNotConnected()
        {
            // Long enough to outlive the give-up below, so the request is certainly still pending
            // when it happens.
            _mockedRippled.AddDelayedResponse(
                "server_info",
                ServerInfoResult(),
                TimeSpan.FromSeconds(30));

            _client = CreateClient(maxReconnectAttempts: 1, stopAfterMaxAttempts: true);

            // Работающий, а потом падающий обработчик - это и есть настоящий случай: подписка,
            // которая какое-то время выполняется и только потом отваливается. Пока она выполняется,
            // сокет открыт, и ожидание подключения успевает вернуться успешно - без этого
            // вызывающий до второй операции просто не доходит.
            _client.OnConnected += async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400));
                throw new InvalidOperationException("handler always fails");
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
                $"Giving up must unblock the caller with NotConnectedException whatever rejected the " +
                $"request it had in flight, got: {connectError?.GetType().Name ?? "no exception"}. " +
                $"Full exception: {connectError}");
        }

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

            Dictionary<string, object> response = await _client.Request(request).Typed();
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

        /// <summary>
        /// With <c>StopAfterMaxAttempts = false</c> there is no give-up branch, so a permanently
        /// failing handler reconnects forever. The delay between attempts must still grow: this
        /// path tears the reconnect loop down and starts it again on every failure, and the loop
        /// derives its delay from the attempt counter alone — seeded from zero it would hammer a
        /// node that accepts TCP but cannot serve requests at a constant ReconnectBaseDelay.
        /// </summary>
        [TestMethod]
        public async Task TestRepeatedOnConnectedFailuresBackOff()
        {
            List<DateTime> attempts = new List<DateTime>();
            TaskCompletionSource<bool> enough =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _client = CreateClient(maxReconnectAttempts: 50, stopAfterMaxAttempts: false);
            _client.connection.OnConnected += () =>
            {
                lock (attempts)
                {
                    attempts.Add(DateTime.UtcNow);
                    if (attempts.Count >= 4)
                    {
                        enough.TrySetResult(true);
                    }
                }

                throw new InvalidOperationException("subscribe failed after connect");
            };

            try
            {
                await _client.Connect();
            }
            catch (Exception)
            {
                // Expected: the first handler invocation throws.
            }

            Task completed = await Task.WhenAny(enough.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.AreSame(completed, enough.Task, "The client stopped retrying a failing handler.");

            List<TimeSpan> gaps = new List<TimeSpan>();
            lock (attempts)
            {
                for (int i = 1; i < attempts.Count; i++)
                {
                    gaps.Add(attempts[i] - attempts[i - 1]);
                }
            }

            // CalcBackoff doubles per attempt off ReconnectBaseDelay (100ms), capped at
            // ReconnectMaxDelay (1s), with 25% jitter. The handler-failure path seeds the counter
            // with its consecutive-failure count, so the delays run 400ms, 800ms, then 1s (capped)
            // — first to last is ~2.5x nominally, and still grows at the jitter extremes.
            // This holds only while the configured cap stays above the earlier backoff values: with
            // a cap at or below 400ms every gap would sit on the cap, and the comparison would come
            // down to which way the jitter fell — a coin flip, not a stable result.
            // Comparing first vs last rather than each consecutive pair keeps the assertion
            // robust: what regressed before was a flat sequence, not the exact multiplier.
            Assert.IsTrue(
                gaps.Count >= 3,
                $"Expected at least 3 gaps between handler invocations, got {gaps.Count}.");
            Assert.IsTrue(
                gaps[gaps.Count - 1] > gaps[0],
                $"Backoff did not grow across consecutive handler failures: {string.Join(", ", gaps)}");
        }
    }
}
