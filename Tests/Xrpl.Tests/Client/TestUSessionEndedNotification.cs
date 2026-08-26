using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests
{
    /// <summary>
    /// Regression tests for issue #123: a session can end without the consumer being told, and the
    /// subscriptions the node held against it are gone the moment it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ChangeServer</c> was the silent case. It marks the old session retiring, which makes the
    /// socket's own close callback return early on purpose, and the only notification it sends is a
    /// <c>Connecting</c> status - the same one a first connection sends. A consumer that resubscribed
    /// on disconnect therefore never resubscribed, the client went on reporting <c>Connected</c>, and
    /// the stream stayed dead for good with nothing in the API saying why.
    /// </para>
    /// <para>
    /// <see cref="OnSessionEnded"/> is the one signal that covers every way a session can end, so
    /// these tests check all of them rather than only the reported one.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUSessionEndedNotification
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

        private XrplClient CreateClient(string url, XrplClient.ClientOptions options = null)
        {
            return new XrplClient(url, options ?? new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(200),
                ReconnectMaxDelay = TimeSpan.FromSeconds(1),
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(3),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                UseCustomPing = false,
            });
        }

        /// <summary>
        /// Waits for <paramref name="condition"/>, polling; returns as soon as it holds.
        /// </summary>
        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
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
        /// The reported case: switching servers from a live connection must say that the session -
        /// and with it the consumer's subscriptions - has ended.
        /// </summary>
        [TestMethod]
        public async Task TestChangeServerAnnouncesThatTheSessionEnded()
        {
            int secondPort = TestUtils.GetFreePort();
            _secondRippled = StartMock(secondPort);

            _client = CreateClient($"ws://127.0.0.1:{_port}");

            List<string> sequence = new List<string>();
            object sequenceLock = new object();

            void Record(string entry)
            {
                lock (sequenceLock)
                {
                    sequence.Add(entry);
                }
            }

            List<SessionEndReason> reasons = new List<SessionEndReason>();
            List<string> descriptions = new List<string>();

            // Recorded, not asserted, inside the handler: a handler that throws is contained by
            // design, so an assertion failing in here would vanish instead of failing the test.
            _client.OnSessionEnded += (reason, description) =>
            {
                lock (sequenceLock)
                {
                    reasons.Add(reason);
                    descriptions.Add(description);
                }

                Record($"ended:{reason}");
                return Task.CompletedTask;
            };
            _client.OnConnected += () =>
            {
                Record("connected");
                return Task.CompletedTask;
            };

            await _client.Connect();
            Assert.IsTrue(_client.connection.IsConnected(), "Precondition: the client must be on the first server.");

            await _client.ChangeServer($"ws://127.0.0.1:{secondPort}");
            Assert.IsTrue(_client.connection.IsConnected(), "Precondition: the client must reach the second server.");

            // The old socket closes in the background, after ChangeServer has returned. Give that
            // close time to arrive: it reaches the same announcement path, and a second event for
            // one session would have the consumer resubscribe twice.
            await Task.Delay(TimeSpan.FromSeconds(1));

            List<string> observed;
            lock (sequenceLock)
            {
                observed = new List<string>(sequence);
            }

            Assert.AreEqual(
                1,
                reasons.Count,
                $"A session ends once and must be announced once. Observed: {string.Join(", ", observed)}.");
            Assert.AreEqual(
                SessionEndReason.ServerChanged,
                reasons[0],
                "ChangeServer is what ended this session.");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(descriptions[0]),
                "The description is what a consumer puts in its log; it must not be empty.");

            // Order matters as much as the event itself: a consumer told about the loss only after
            // OnConnected for the new session would resubscribe and then be told its subscriptions
            // are gone - and would stop, exactly where it started.
            CollectionAssert.AreEqual(
                new List<string> { "connected", $"ended:{SessionEndReason.ServerChanged}", "connected" },
                observed,
                $"The end of the old session must be announced before the new one connects. Observed: {string.Join(", ", observed)}.");
        }

        /// <summary>
        /// A caller closing the connection ends the session too, and is told so with a reason that
        /// says it was their own doing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both announcements are asserted, and the point is that each comes exactly once however
        /// the receive loop happens to end. Cancelling a parked receive throws and the loop reports
        /// the close from a catch; when the response that woke <c>Connect()</c> resumed the caller
        /// inline on the receive-loop thread instead, the close happens inside that continuation
        /// and the loop leaves by its <c>while</c> condition. That exit used to report nothing at
        /// all - the silent path this class was written to close - and now reports the same way.
        /// </para>
        /// <para>
        /// Fixing that exit is what makes this path work at all: the session-ended announcement on
        /// a user close rides on the same callback, so while the loop was silent the consumer heard
        /// neither event.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUserDisconnectAnnouncesThatTheSessionEnded()
        {
            _client = CreateClient($"ws://127.0.0.1:{_port}");

            List<SessionEndReason> reasons = new List<SessionEndReason>();
            object gate = new object();
            int disconnects = 0;
            _client.OnSessionEnded += (reason, description) =>
            {
                lock (gate)
                {
                    reasons.Add(reason);
                }

                return Task.CompletedTask;
            };
            _client.OnDisconnect += (code, description) =>
            {
                Interlocked.Increment(ref disconnects);
                return Task.CompletedTask;
            };

            await _client.Connect();
            Assert.IsTrue(_client.connection.IsConnected(), "Precondition: the client must be connected.");

            await _client.Disconnect();
            _client = null; // Already down; cleanup must not disconnect it a second time.

            // Disconnect() returns as soon as it has asked the socket to close; the callback that
            // reports it lands afterwards. The fixed wait that follows is not for the first event
            // but for a second: both counts below are asserted to be one, and a duplicate needs
            // somewhere to show up.
            await WaitUntilAsync(() => { lock (gate) { return reasons.Count > 0; } }, TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            lock (gate)
            {
                Assert.AreEqual(
                    1,
                    reasons.Count,
                    $"A user disconnect ends exactly one session (OnDisconnect fired {Volatile.Read(ref disconnects)} time(s)).");
                Assert.AreEqual(
                    SessionEndReason.UserDisconnected,
                    reasons[0],
                    "Nothing failed here - the caller asked for it, and a consumer may well want to tell the difference.");
            }

            Assert.AreEqual(
                1,
                Volatile.Read(ref disconnects),
                "A closed socket is reported once, whichever way the receive loop ended.");
        }

        /// <summary>
        /// A connection attempt that never succeeded had no subscriptions, so there is nothing to
        /// announce - and announcing it anyway would have consumers resubscribing against a client
        /// that was never up, once per retry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A session object exists all the same: it is created before <c>ws.Connect()</c> is
        /// called, so it long outlives the question of whether a connection happened. The failed
        /// attempt still reaches the close callback - <c>closes</c> below is two even here - which
        /// is why the announcement has to ask whether the socket ever opened rather than whether a
        /// session exists.
        /// </para>
        /// <para>
        /// <b>This test is worth less on Windows than on Linux.</b> Whether the close callback
        /// arrives while its session is still the active one is a matter of timing: on Linux the
        /// ids match and the defect showed as three announcements for three retries, which is how
        /// CI caught it; on Windows the next retry has already installed its session by then, the
        /// ids miss, and the count is zero with or without the fix. Removing the fix locally and
        /// rerunning is therefore not a check - it passes either way here.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestFailedConnectAnnouncesNothing()
        {
            int deadPort = TestUtils.GetFreePort(); // nothing is listening there

            _client = CreateClient($"ws://127.0.0.1:{deadPort}", new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 2,
                StopAfterMaxAttempts = true,
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(2),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                UseCustomPing = false,
            });

            int announced = 0;
            int closes = 0;
            _client.OnSessionEnded += (reason, description) =>
            {
                Interlocked.Increment(ref announced);
                return Task.CompletedTask;
            };
            _client.OnDisconnect += (code, description) =>
            {
                Interlocked.Increment(ref closes);
                return Task.CompletedTask;
            };

            try
            {
                await _client.Connect();
            }
            catch (Exception)
            {
                // Expected - nothing is listening. What matters is that no session was announced.
            }

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.IsTrue(
                Volatile.Read(ref closes) > 0,
                "The attempt must actually have failed and been reported, or this test proves nothing.");
            Assert.AreEqual(
                0,
                Volatile.Read(ref announced),
                "No session was ever established, so none can have ended.");
        }

        /// <summary>
        /// The fast-reconnect path - a ping timeout or a dead-quiet socket - retires the session
        /// just as <c>ChangeServer</c> does, and was just as quiet about the subscriptions going
        /// with it. Its <c>RestoringConnection</c> status says the connection is being rebuilt, not
        /// that everything bound to the old one is gone.
        /// </summary>
        [TestMethod]
        public async Task TestFastReconnectAnnouncesThatTheSessionEnded()
        {
            using SilentOnPingServer server = new SilentOnPingServer();

            _client = CreateClient(server.Url, new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(500),
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(3),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                // The two knobs that exist so this path is reachable from a test in under a second
                // rather than after a minute of real silence.
                UseCustomPing = true,
                HealthCheckInterval = TimeSpan.FromMilliseconds(200),
                InactivityTimeout = TimeSpan.FromMilliseconds(500),
            });

            List<SessionEndReason> reasons = new List<SessionEndReason>();
            object gate = new object();
            _client.OnSessionEnded += (reason, description) =>
            {
                lock (gate)
                {
                    reasons.Add(reason);
                }

                return Task.CompletedTask;
            };

            await _client.Connect();
            Assert.IsTrue(_client.connection.IsConnected(), "Precondition: the client must be connected.");

            // The server never answers a ping, so nothing arrives, the health check sees silence
            // past the inactivity limit and hands the connection to the fast-reconnect path.
            await WaitUntilAsync(() => { lock (gate) { return reasons.Count > 0; } }, TimeSpan.FromSeconds(15));

            lock (gate)
            {
                Assert.IsTrue(
                    reasons.Count > 0,
                    "A session retired by the fast-reconnect path must be announced like any other.");
                Assert.AreEqual(
                    SessionEndReason.ConnectionLost,
                    reasons[0],
                    "Nobody asked for this one - the connection went quiet and the SDK rebuilt it.");
            }
        }
    }
}
