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
    /// Which path produces which outcome - see <c>specs/2026-09-09-connection-outcome-api.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The types themselves are covered in <c>TestUConnectionOutcomeTypes</c>. What is pinned here
    /// is the mapping: an event of the connection, and the type a caller gets for it. Every
    /// assertion is on a type or on a value, never on message text - which is the entire point of
    /// the change.
    /// </para>
    /// <para>
    /// Each test also asserts that the base type still catches, because the promise made to
    /// consumers is that this is additive: a <c>catch (NotConnectedException)</c> written against
    /// 11.4.0 keeps working.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUConnectionOutcomes
    {
        private XrplClient _client;

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

        [TestCleanup]
        public async Task MyTestCleanup()
        {
            if (_client != null)
            {
                await _client.Disconnect();
                _client = null;
            }
        }

        /// <summary>
        /// A client that was never told to connect says so, rather than saying it is disconnected.
        /// </summary>
        /// <remarks>
        /// "Nothing is in progress" is a distinct, actionable answer - the caller has to call
        /// <c>Connect()</c> - and it is neither "the consumer took the client down" nor "the
        /// reconnect loop gave up". It used to arrive as a bare <see cref="NotConnectedException"/>
        /// alongside both of those.
        /// </remarks>
        [TestMethod]
        public async Task TestUAClientThatNeverConnectedReportsThatNothingIsInProgress()
        {
            int port = TestUtils.GetFreePort(); // nothing is listening, and nothing will be asked to
            _client = new XrplClient($"ws://127.0.0.1:{port}");

            NotConnectingException error = await Assert.ThrowsExactlyAsync<NotConnectingException>(
                async () => await _client.connection.WaitForConnectionAsync(TimeSpan.FromSeconds(1)));

            Assert.IsInstanceOfType<NotConnectedException>(error, "catch (NotConnectedException) must keep catching this.");
        }

        /// <summary>
        /// A caller already waiting when the reconnect loop spends its budget is told the budget is
        /// spent, and how big it was.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the outcome that justifies a failover, and the one it was impossible to
        /// recognise: it arrived as the same <see cref="NotConnectedException"/> as a consumer's
        /// own <c>Disconnect()</c>, which calls for the opposite reaction.
        /// </para>
        /// <para>
        /// The wait has to begin before the loop gives up. Once the loop has released its
        /// cancellation source and reported <c>Disconnected</c>, a fresh call finds no attempt in
        /// progress at all and is answered by <see cref="NotConnectingException"/> - a different,
        /// equally correct answer to a different question. That is why the waiter here is
        /// <c>ChangeServer</c>'s own: it starts the attempt and then waits for it, so it is
        /// already parked when the loop it left behind runs out of budget. Stopping a server and
        /// racing to park a waiter before the loop finishes would test the same thing by timing.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUAWaiterLearnsTheReconnectBudgetWasSpent()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 2,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
                    UseCustomPing = false,
                });

                await _client.Connect();
                Assert.IsTrue(_client.connection.IsConnected(), "Precondition: connected to the mock.");

                int deadPort = TestUtils.GetFreePort(); // nothing is listening there, and never will be

                ReconnectExhaustedException error = await Assert.ThrowsExactlyAsync<ReconnectExhaustedException>(
                    async () => await _client.connection.ChangeServer($"ws://127.0.0.1:{deadPort}"));

                Assert.AreEqual(2, error.MaxAttempts, "The budget the client was configured with.");
                Assert.AreEqual(
                    error.MaxAttempts,
                    error.Attempts,
                    "Attempts made, not the raw counter - which stands one past the budget when the loop stops.");
                Assert.IsInstanceOfType<NotConnectedException>(error, "catch (NotConnectedException) must keep catching this.");
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// Giving up on a broken <c>OnConnected</c> handler says the handler broke - not that the
        /// consumer disconnected the client.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The give-up path ends by calling <c>Disconnect()</c> itself, which sets the same
        /// permanently-disconnected flag a consumer's own <c>Disconnect()</c> sets. Every point
        /// that reads that flag then answered "the client has been disconnected" - the one thing
        /// this event is not. The node is answering; what failed is code on this side, so a
        /// consumer reacting by failing over would be leaving a healthy server.
        /// </para>
        /// <para>
        /// The distinction was invisible and timing-dependent: the same broken handler produced one
        /// type when it failed immediately and another when it failed a moment later, because only
        /// the second left a request in flight to be rejected with the real reason.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUGivingUpOnABrokenConnectHandlerSaysTheHandlerBroke()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 2,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
                    UseCustomPing = false,
                });

                InvalidOperationException thrownByHandler = new InvalidOperationException("handler is permanently broken");
                _client.connection.OnConnected += () => throw thrownByHandler;

                ConnectHandlerFailedException error = await Assert.ThrowsExactlyAsync<ConnectHandlerFailedException>(
                    async () => await _client.Connect());

                Assert.IsGreaterThanOrEqualTo(1, error.Failures, "The handler failed at least once before the client gave up.");
                Assert.AreSame(thrownByHandler, error.InnerException, "The handler's own failure is what says why.");
                Assert.IsInstanceOfType<NotConnectedException>(error, "catch (NotConnectedException) must keep catching this.");
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// An operation that was overtaken says what overtook it and where the client ended up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The second <c>ChangeServer</c> is issued from the first one's own session-ended handler,
        /// which lands it inside the first one's yields every time - the shape a consumer actually
        /// hits, and deterministic where a race would not be.
        /// </para>
        /// <para>
        /// Before this, the overtaken call got a bare <see cref="OperationCanceledException"/>,
        /// indistinguishable from the caller's own token being cancelled. A consumer that showed
        /// the user "could not connect, pick another node" for it was reporting a failure on a
        /// client that was at that moment connecting normally somewhere else.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUAnOvertakenSwitchNamesTheSwitchThatWon()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();
            int thirdPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            CreateMockRippled second = StartMock(secondPort);
            CreateMockRippled third = StartMock(thirdPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                string thirdUrl = $"ws://127.0.0.1:{thirdPort}";
                int nested = 0;
                _client.OnSessionEnded += async (reason, _) =>
                {
                    if (reason == SessionEndReason.ServerChanged && Interlocked.Exchange(ref nested, 1) == 0)
                    {
                        await _client.connection.ChangeServer(thirdUrl);
                    }
                };

                ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                    async () => await _client.connection.ChangeServer($"ws://127.0.0.1:{secondPort}"));

                Assert.AreEqual(ConnectionTransitionKind.ChangeServer, error.Kind);
                Assert.AreEqual(thirdUrl, error.SupersededBy, "The caller is told where the client actually is.");
                Assert.IsInstanceOfType<OperationCanceledException>(
                    error,
                    "catch (OperationCanceledException) must keep catching this.");
            }
            finally
            {
                first.Stop();
                second.Stop();
                third.Stop();
            }
        }

        /// <summary>
        /// A request refused because the caller asked not to wait says that, and not that the
        /// endpoint is dead.
        /// </summary>
        /// <remarks>
        /// <c>ImmediateFail</c> is a policy the consumer chose, and the refusal it produces says
        /// nothing about the server: the connection was being rebuilt and the caller asked not to
        /// wait for it. Retrying once connected is the reaction. It used to arrive as the same
        /// <see cref="NotConnectedException"/> as "the reconnect loop gave up", which calls for a
        /// failover instead.
        /// </remarks>
        [TestMethod]
        public async Task TestUARequestRefusedByPolicySaysSoRatherThanBlamingTheServer()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            CreateMockRippled second = StartMock(secondPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    RequestPolicy = RequestFailurePolicy.ImmediateFail,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                // Issued from inside the switch, where the old socket is gone and the new one is not
                // open yet - the window the policy exists for.
                Exception refusal = null;
                _client.OnSessionEnded += async (reason, _) =>
                {
                    if (reason == SessionEndReason.ServerChanged && refusal == null)
                    {
                        try
                        {
                            await _client.Request(new Dictionary<string, object> { { "command", "server_info" } });
                        }
                        catch (Exception error)
                        {
                            refusal = error;
                        }
                    }
                };

                await _client.connection.ChangeServer($"ws://127.0.0.1:{secondPort}");

                Assert.IsInstanceOfType<RequestRefusedException>(
                    refusal,
                    $"A request refused by ImmediateFail must say so, got: {refusal?.GetType().Name ?? "no exception"}.");
                Assert.IsInstanceOfType<NotConnectedException>(refusal, "catch (NotConnectedException) must keep catching this.");
            }
            finally
            {
                first.Stop();
                second.Stop();
            }
        }

        /// <summary>
        /// Switching servers survives a connection that settles on the second try, the way
        /// connecting does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Connect()</c> is two operations - the connection, and the <c>server_info</c> that
        /// reads the network id - and it has carried the second across a teardown since 11.4.0,
        /// because a socket really does open for a moment before a failing handler brings it down.
        /// <c>ChangeServer</c> is the same two operations against a different server and had no
        /// such protection: it read the network id once, directly, so a connection that needed a
        /// second attempt failed the switch.
        /// </para>
        /// <para>
        /// The second server drops the connection the first time it is asked for
        /// <c>server_info</c> and serves normally afterwards, which puts the teardown exactly
        /// between "the switch is connected" and "the switch has read the network id" - the only
        /// window where the two behave differently.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUSwitchingServersSurvivesAConnectionThatSettlesOnRetry()
        {
            int firstPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            DropsFirstServerInfoServer dropping = new DropsFirstServerInfoServer();

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 4,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(15),
                    UseCustomPing = false,
                });

                await _client.Connect();

                await _client.ChangeServer(dropping.Url);

                Assert.IsTrue(_client.connection.IsConnected(), "The switch has to end connected, not thrown.");
            }
            finally
            {
                first.Stop();
                dropping.Dispose();
            }
        }

        /// <summary>
        /// A request swept by a server switch is told which switch took the connection, and where.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the failure consumers meet most often - not an overtaken transition, but their
        /// own request dying while the connection moved underneath it. It arrived as a bare
        /// <see cref="OperationCanceledException"/> reading "Connection was intentionally closed",
        /// identical to a request the caller cancelled itself.
        /// </para>
        /// <para>
        /// The new type still derives from <see cref="OperationCanceledException"/>, so a caller
        /// that treated this as cancellation keeps working unchanged and the task keeps the status
        /// it had. That is asserted here alongside the new information, because it is the promise
        /// that makes the change safe to ship in a minor version.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUARequestSweptByASwitchNamesTheSwitch()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();

            CreateMockRippled first = new CreateMockRippled(firstPort) { suppressOutput = true };
            first.AddResponse("server_info", ServerInfoResponse());
            // Sat on long enough that the switch below lands while the request is still in flight.
            first.AddDelayedResponse("ledger", ServerInfoResponse(), TimeSpan.FromSeconds(10));
            new Thread(() => first.Start()) { IsBackground = true }.Start();

            CreateMockRippled second = StartMock(secondPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                Task inFlight = _client.Request(new Dictionary<string, object> { { "command", "ledger" } });
                await Task.Delay(TimeSpan.FromMilliseconds(300)); // it is on the wire, and unanswered

                string secondUrl = $"ws://127.0.0.1:{secondPort}";
                await _client.connection.ChangeServer(secondUrl);

                ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                    async () => await inFlight);

                Assert.AreEqual(ConnectionTransitionKind.ChangeServer, error.Kind);
                Assert.AreEqual(secondUrl, error.SupersededBy);
                Assert.IsInstanceOfType<OperationCanceledException>(
                    error,
                    "catch (OperationCanceledException) must keep catching a swept request.");
            }
            finally
            {
                first.Stop();
                second.Stop();
            }
        }

        /// <summary>
        /// The notification that says the client stopped also says why it stopped.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Disconnected</c> is announced from ten places - a consumer's disconnect, a first
        /// connection that failed, a connection closed for good, a broken handler, and the reconnect
        /// loop running out of budget - and none of them carried anything but text. "Still trying"
        /// against "gave up" was derivable only from the absence of <c>ReconnectInfo</c>, which is
        /// also what a client that never had a loop looks like.
        /// </para>
        /// <para>
        /// The reason goes on the notification rather than into <c>ReconnectInfo</c>: filling that
        /// in on a terminal notification would take <c>Reconnect != null</c>, which consumers read
        /// as "a loop is running", and give it a second meaning. It stays null here, and that is
        /// asserted.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUTheTerminalNotificationSaysWhyTheClientStopped()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            List<ConnectionStatusInfo> statuses = new List<ConnectionStatusInfo>();

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 2,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
                    UseCustomPing = false,
                });

                await _client.Connect();

                _client.connection.OnConnectionStatus += status =>
                {
                    lock (statuses)
                    {
                        statuses.Add(status);
                    }
                };

                int deadPort = TestUtils.GetFreePort();
                try
                {
                    await _client.connection.ChangeServer($"ws://127.0.0.1:{deadPort}");
                }
                catch (ReconnectExhaustedException)
                {
                    // The subject of this test is the notification, not the exception.
                }

                ConnectionStatusInfo terminal;
                lock (statuses)
                {
                    terminal = statuses.FindLast(s => s.ConnectionState == XrpConnectionState.Disconnected);
                }

                Assert.IsNotNull(terminal, "The client has to announce that it stopped.");
                Assert.AreEqual(
                    ConnectionStopReason.ReconnectExhausted,
                    terminal.StopReason,
                    "Giving up on the budget is a different event from the consumer disconnecting.");
                Assert.IsNull(
                    terminal.Reconnect,
                    "Reconnect stays null on a terminal notification, so 'a loop is running' keeps its one meaning.");
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// A consumer's own disconnect is named as such, and is not confused with giving up.
        /// </summary>
        [TestMethod]
        public async Task TestUAConsumerDisconnectIsNamedInTheStatusStream()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            List<ConnectionStatusInfo> statuses = new List<ConnectionStatusInfo>();

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                _client.connection.OnConnectionStatus += status =>
                {
                    lock (statuses)
                    {
                        statuses.Add(status);
                    }
                };

                await _client.Disconnect();
                _client = null;

                ConnectionStatusInfo terminal;
                lock (statuses)
                {
                    terminal = statuses.FindLast(s => s.ConnectionState == XrpConnectionState.Disconnected);
                }

                Assert.IsNotNull(terminal);
                Assert.AreEqual(ConnectionStopReason.UserDisconnected, terminal.StopReason);
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// "Did it come back?" is answerable without catching anything - and the answer says which
        /// of the ways it did not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both known consumers wrapped the throwing wait to get a value back, because "it did not
        /// come back in time" is an answer a caller has to act on rather than an exceptional event.
        /// A <c>bool</c> would have been the obvious shape and the wrong one: it folds "timed out",
        /// "gave up" and "nothing is running" into one <c>false</c>, which is the problem this
        /// whole change is about, moved into a new method.
        /// </para>
        /// <para>
        /// The caller's own cancellation and an invalid timeout stay exceptions: the first is the
        /// .NET convention, the second is a mistake by the caller rather than an outcome of the
        /// connection.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUTheOutcomeOfAWaitIsAValueAndNamesTheCase()
        {
            int port = TestUtils.GetFreePort();
            _client = new XrplClient($"ws://127.0.0.1:{port}");

            ConnectionWaitOutcome outcome =
                await _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(1));

            Assert.AreEqual(ConnectionWaitOutcome.NotConnecting, outcome);
        }

        /// <summary>
        /// It is callable through the interface and through the class alike.
        /// </summary>
        /// <remarks>
        /// The interface member is defaulted - forwarding to <c>connection</c> is the only
        /// implementation that means anything, and an external implementer of
        /// <see cref="IXrplClient"/> should not have to add one. A default member is only visible
        /// through an interface-typed reference, though, so the client carries its own as well;
        /// both are exercised here because a consumer holding either must be able to ask.
        /// </remarks>
        [TestMethod]
        public async Task TestUTheOutcomeIsReachableThroughTheInterfaceAndTheClass()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                IXrplClient asInterface = _client;

                Assert.AreEqual(
                    ConnectionWaitOutcome.Connected,
                    await asInterface.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(
                    ConnectionWaitOutcome.Connected,
                    await _client.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// A request swept by the client rebuilding its own connection names that rebuild.
        /// </summary>
        /// <remarks>
        /// The fourth kind, and the one a consumer must not read as a failure of the node: the
        /// health check found the connection silent and replaced it, the server is where it always
        /// was, and the request is worth sending again once the new connection is up. Reached
        /// through the health check because that is the only path that rebuilds a connection
        /// nobody asked it to rebuild.
        /// </remarks>
        [TestMethod]
        public async Task TestUARequestSweptByAReconnectNamesTheReconnect()
        {
            using SilentOnPingAndLedgerServer silent = new SilentOnPingAndLedgerServer();

            _client = new XrplClient(silent.Url, new XrplClient.ClientOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(500),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                UseCustomPing = true,
                HealthCheckInterval = TimeSpan.FromMilliseconds(200),
                InactivityTimeout = TimeSpan.FromMilliseconds(500),
            });

            await _client.Connect();

            Task inFlight = _client.Request(new Dictionary<string, object> { { "command", "ledger" } });

            ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                async () => await inFlight);

            Assert.AreEqual(ConnectionTransitionKind.Reconnect, error.Kind);
            Assert.IsInstanceOfType<OperationCanceledException>(error);
        }

        /// <summary>
        /// A waiter parked while the client gives up on a broken handler is answered, not left to
        /// time out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two things have to line up for this to work, and neither is obvious. The give-up
        /// announces itself before it performs the disconnect that records why, so a waiter woken
        /// by that announcement finds nothing terminal yet and parks again. What has to reach it
        /// then is the disconnect's own notification - and that one says <c>Disconnected</c> on a
        /// client already reported as <c>Disconnected</c>, so the status stream suppresses it as a
        /// duplicate.
        /// </para>
        /// <para>
        /// Waking waiters therefore cannot be a side effect of emitting a status event: whether an
        /// event is worth showing a consumer and whether the state changed are different
        /// questions. The timeout here is long against a give-up that takes well under a second,
        /// so "answered" and "gave up waiting" cannot be confused.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUAWaiterIsAnsweredWhenTheClientGivesUpOnItsHandler()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 2,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(20),
                    UseCustomPing = false,
                });

                _client.connection.OnConnected += () => throw new InvalidOperationException("permanently broken");

                // Parked from the status stream rather than after a sleep: the socket is open for
                // the moment the handler runs in, so a waiter started by the clock can find the
                // client connected and answer that instead. RestoringConnection is the state where
                // there is no socket and the client is between attempts - which is where a real
                // caller waiting for the connection to come back sits.
                Task<ConnectionWaitOutcome> waiting = null;
                _client.connection.OnConnectionStatus += status =>
                {
                    if (status.ConnectionState == XrpConnectionState.RestoringConnection
                        && Interlocked.CompareExchange(ref waiting, null, null) == null)
                    {
                        Interlocked.CompareExchange(
                            ref waiting,
                            _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(20)),
                            null);
                    }
                };

                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

                await Assert.ThrowsExactlyAsync<ConnectHandlerFailedException>(async () => await _client.Connect());

                Task<ConnectionWaitOutcome> parked = Interlocked.CompareExchange(ref waiting, null, null);
                Assert.IsNotNull(parked, "Precondition: the client has to report RestoringConnection at least once.");

                ConnectionWaitOutcome outcome = await parked;
                clock.Stop();

                // Not asserted as one particular outcome: the socket really does open for the
                // moment the handler runs in, so a waiter can legitimately be answered
                // "Connected" by an attempt that is about to fail, or "ConnectHandlerFailed" by
                // the give-up. Which one wins is timing. What must never happen is neither - a
                // waiter left parked because the wake-up that concerned it was swallowed.
                Assert.AreNotEqual(
                    ConnectionWaitOutcome.TimedOut,
                    outcome,
                    "The waiter sat out its whole timeout: a state change it was waiting for did not reach it.");
                Assert.IsLessThan(
                    TimeSpan.FromSeconds(10),
                    clock.Elapsed,
                    "The waiter has to be answered by the client, not by its own deadline.");
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// A client that spent its reconnect budget stays stopped.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>StopAfterMaxAttempts</c> is a promise that the client will stop asking, and the
        /// consumer is told it has: <c>Disconnected</c> with
        /// <see cref="ConnectionStopReason.ReconnectExhausted"/>. A second series behind that
        /// notification breaks the promise twice over - the budget is spent again without being
        /// granted again, and a consumer that failed over on the first notification now has a
        /// client quietly dialling the endpoint it moved away from.
        /// </para>
        /// <para>
        /// What made it possible: the loop's exit clears the two fields that say "a sequence is
        /// running for this generation", which is exactly what "no sequence is running" looks
        /// like. The close of the last failed attempt arrives after that and is indistinguishable
        /// from the first one.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUAClientThatSpentItsReconnectBudgetStaysStopped()
        {
            SilentOnPingAndLedgerServer silent = new SilentOnPingAndLedgerServer();

            List<ConnectionStatusInfo> statuses = new List<ConnectionStatusInfo>();

            try
            {
                _client = new XrplClient(silent.Url, new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 1,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(1),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(5),
                    UseCustomPing = false,
                });

                await _client.Connect();

                _client.connection.OnConnectionStatus += status =>
                {
                    lock (statuses)
                    {
                        statuses.Add(status);
                    }
                };

                silent.Dispose();

                // Long enough for a second series to have run and announced itself: the budget
                // above is spent in a few hundred milliseconds.
                await Task.Delay(TimeSpan.FromSeconds(4));

                int gaveUp;
                string trace;
                lock (statuses)
                {
                    gaveUp = statuses.FindAll(s =>
                        s.ConnectionState == XrpConnectionState.Disconnected &&
                        s.StopReason == ConnectionStopReason.ReconnectExhausted).Count;
                    trace = string.Join(" | ", statuses.ConvertAll(x => $"{x.ConnectionState}/{x.StopReason}: {x.Message}"));
                }

                Assert.AreEqual(1, gaveUp, $"Giving up is announced once and meant once. Sequence was: {trace}");
                Assert.IsFalse(_client.connection.IsConnected());

                // The other half of the promise, and the risk of keeping it: stopping must not mean
                // wedged. Asking again is the consumer's decision, and a consumer command begins a
                // new generation - which is what lifts the refusal, with no flag to reset and no
                // way for it to outlive the sequence it belongs to.
                int livePort = TestUtils.GetFreePort();
                CreateMockRippled live = StartMock(livePort);
                try
                {
                    await _client.ChangeServer($"ws://127.0.0.1:{livePort}");
                    Assert.IsTrue(
                        _client.connection.IsConnected(),
                        "A client that stopped asking on its own must still answer the consumer asking.");
                }
                finally
                {
                    live.Stop();
                }
            }
            finally
            {
                silent.Dispose();
            }
        }

        /// <summary>
        /// The same answer whether the handler fails at once or a moment later.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The timing decides which path reports the failure. A handler that throws at once leaves
        /// nothing in flight and the caller is answered by the wait; one that works for a moment
        /// first - a subscribe that gets some way in before falling over - lets the caller reach
        /// the network-id read, and the teardown then finds a request in flight to reject. Both
        /// used to be the same bare exception, and attaching the reason to only one of them would
        /// have handed the caller two different types for one scenario depending on how fast the
        /// machine was.
        /// </para>
        /// <para>
        /// Holding <c>server_info</c> back is what makes the second path certain rather than lucky:
        /// answered at once, the request is gone before the handler gives up and the test would
        /// pass without ever exercising the case.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUABrokenConnectHandlerAnswersTheSameWhicheverWayItFails()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = new CreateMockRippled(port) { suppressOutput = true };
            mock.AddDelayedResponse("server_info", ServerInfoResponse(), TimeSpan.FromSeconds(5));
            new Thread(() => mock.Start()) { IsBackground = true }.Start();

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 1,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
                    UseCustomPing = false,
                });

                InvalidOperationException thrownByHandler = new InvalidOperationException("broken, but not straight away");
                _client.connection.OnConnected += async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400));
                    throw thrownByHandler;
                };

                ConnectHandlerFailedException error = await Assert.ThrowsExactlyAsync<ConnectHandlerFailedException>(
                    async () => await _client.Connect());

                Assert.AreSame(thrownByHandler, error.InnerException);
                Assert.IsGreaterThanOrEqualTo(1, error.Failures);
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// The status stream names a broken handler as such, and says nothing about a notification
        /// that is not terminal.
        /// </summary>
        /// <remarks>
        /// The two halves belong together: a reason that appeared on every notification would be
        /// as useless as none at all, and <see cref="ConnectionStopReason.None"/> on the
        /// <c>RestoringConnection</c> that precedes the give-up is what lets a consumer treat the
        /// field as "this one is terminal, and here is why".
        /// </remarks>
        [TestMethod]
        public async Task TestUTheStatusStreamNamesABrokenHandlerAndOnlyWhenTerminal()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            List<ConnectionStatusInfo> statuses = new List<ConnectionStatusInfo>();

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 2,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
                    UseCustomPing = false,
                });

                _client.connection.OnConnectionStatus += status =>
                {
                    lock (statuses)
                    {
                        statuses.Add(status);
                    }
                };

                _client.connection.OnConnected += () => throw new InvalidOperationException("permanently broken");

                try
                {
                    await _client.Connect();
                }
                catch (ConnectHandlerFailedException)
                {
                    // The subject here is the status stream.
                }

                ConnectionStatusInfo terminal;
                List<ConnectionStatusInfo> nonTerminal;
                lock (statuses)
                {
                    terminal = statuses.FindLast(s => s.ConnectionState == XrpConnectionState.Disconnected);
                    nonTerminal = statuses.FindAll(s => s.ConnectionState != XrpConnectionState.Disconnected);
                }

                string seq;
                lock (statuses)
                {
                    seq = string.Join(" | ", statuses.ConvertAll(x => $"{x.ConnectionState}/{x.StopReason}"));
                }

                Assert.IsNotNull(terminal);

                // The last word, deliberately: the give-up announces the handler failure and then
                // performs a disconnect of its own, which announces again. Both have to name the
                // same event, or the status stream ends by contradicting the exception the same
                // failure produced. Reading the last one is what catches that.
                Assert.AreEqual(
                    ConnectionStopReason.ConnectHandlerFailed,
                    terminal.StopReason,
                    $"The last thing said about a broken handler must still be the handler. Sequence was: {seq}");

                foreach (ConnectionStatusInfo status in nonTerminal)
                {
                    Assert.AreEqual(
                        ConnectionStopReason.None,
                        status.StopReason,
                        $"A {status.ConnectionState} notification is not an ending and must not claim a reason.");
                }
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// A switch overtaken at the client level is reported, not retried away.
        /// </summary>
        /// <remarks>
        /// <c>XrplClient.ChangeServer</c> reads the network id after the switch and carries that
        /// read across a teardown, which means it has a retry loop around an operation that can
        /// fail because the client moved. The loop must not swallow a supersession: the caller's
        /// switch did not happen, and asking again would read the network id of a server it never
        /// named. The line is drawn by the kind of transition, which is why this is asserted
        /// through the client rather than through the connection.
        /// </remarks>
        [TestMethod]
        public async Task TestUAnOvertakenSwitchIsReportedThroughTheClientToo()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();
            int thirdPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            CreateMockRippled second = StartMock(secondPort);
            CreateMockRippled third = StartMock(thirdPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                string thirdUrl = $"ws://127.0.0.1:{thirdPort}";
                int nested = 0;
                _client.OnSessionEnded += async (reason, _) =>
                {
                    if (reason == SessionEndReason.ServerChanged && Interlocked.Exchange(ref nested, 1) == 0)
                    {
                        await _client.connection.ChangeServer(thirdUrl);
                    }
                };

                ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                    async () => await _client.ChangeServer($"ws://127.0.0.1:{secondPort}"));

                Assert.AreEqual(ConnectionTransitionKind.ChangeServer, error.Kind);
                Assert.AreEqual(thirdUrl, error.SupersededBy);
            }
            finally
            {
                first.Stop();
                second.Stop();
                third.Stop();
            }
        }

        /// <summary>
        /// Starts a mock that sits on one command, so a request can still be in flight when
        /// something happens to the connection.
        /// </summary>
        private static CreateMockRippled StartMockHoldingOnto(int port, string command, TimeSpan delay)
        {
            CreateMockRippled mock = new CreateMockRippled(port) { suppressOutput = true };
            mock.AddResponse("server_info", ServerInfoResponse());
            mock.AddDelayedResponse(command, ServerInfoResponse(), delay);

            new Thread(() => mock.Start()) { IsBackground = true }.Start();
            return mock;
        }

        private XrplClient ClientHoldingRequests(int port) =>
            new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                UseCustomPing = false,
            });

        /// <summary>
        /// A request swept by the consumer disconnecting is told so, and is still a cancellation.
        /// </summary>
        /// <remarks>
        /// The kind matters more here than anywhere: a request that died because the consumer took
        /// the client down needs no retry and no failover, and it used to be indistinguishable from
        /// one that died because the connection moved. It stays an
        /// <see cref="OperationCanceledException"/> rather than becoming a
        /// <see cref="ClientDisconnectedException"/> - the request was cancelled, and changing that
        /// would turn a cancellation into a failure for every caller that already handles it.
        /// </remarks>
        [TestMethod]
        public async Task TestUARequestSweptByADisconnectNamesTheDisconnect()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMockHoldingOnto(port, "ledger", TimeSpan.FromSeconds(10));

            try
            {
                _client = ClientHoldingRequests(port);
                await _client.Connect();

                Task inFlight = _client.Request(new Dictionary<string, object> { { "command", "ledger" } });
                await Task.Delay(TimeSpan.FromMilliseconds(300));

                await _client.Disconnect();
                _client = null;

                ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                    async () => await inFlight);

                Assert.AreEqual(ConnectionTransitionKind.Disconnect, error.Kind);
                Assert.IsNull(error.SupersededBy, "A disconnect took the client nowhere.");
                Assert.IsInstanceOfType<OperationCanceledException>(error);
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// <c>Connect()</c> on a client that is already connected disturbs nothing.
        /// </summary>
        /// <remarks>
        /// Written while trying to reach the sweep that <c>Connect()</c> performs, and kept because
        /// of what it found instead: <c>Connect()</c> returns at once when the client is already
        /// connected, so it never gets as far as taking the socket over. The sweep is therefore
        /// reachable only from a client that is not connected - where there is no request in flight
        /// to sweep - and the <c>Connect</c> kind is exercised through supersession instead. What
        /// is worth pinning here is that a redundant <c>Connect()</c> does not quietly kill the
        /// requests a caller has outstanding.
        /// </remarks>
        [TestMethod]
        public async Task TestURedundantConnectDoesNotDisturbRequestsInFlight()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMockHoldingOnto(port, "ledger", TimeSpan.FromSeconds(1));

            try
            {
                _client = ClientHoldingRequests(port);
                await _client.Connect();

                Task inFlight = _client.Request(new Dictionary<string, object> { { "command", "ledger" } });
                await Task.Delay(TimeSpan.FromMilliseconds(200));

                await _client.connection.Connect(CancellationToken.None);

                await inFlight; // answered by the connection it was written to
                Assert.IsTrue(_client.connection.IsConnected());
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// A request killed by the connection failing on its own stays a plain cancellation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the deliberate border of the change. A connection that failed has no transition
        /// to name and no destination to point at, so inventing one would be worse than saying
        /// little: a consumer switching on <see cref="ConnectionTransitionKind"/> would be told the
        /// client moved somewhere when nothing moved it.
        /// </para>
        /// <para>
        /// What such a request actually gets is <see cref="DisconnectedException"/>, reported by
        /// the close path with the code and reason the peer gave - measured here rather than
        /// assumed, because the sweep this test was written to check turned out not to be the one
        /// that answers first. The assertion that matters either way is the negative one: no
        /// transition is named.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUARequestKilledByANetworkDropNamesNoTransition()
        {
            DropsFirstServerInfoServer dropping = new DropsFirstServerInfoServer();

            try
            {
                _client = new XrplClient(dropping.Url, new XrplClient.ClientOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    MaxReconnectAttempts = 4,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                // The connection alone, so nothing asks for server_info before the test does.
                await _client.connection.Connect(CancellationToken.None);

                Exception failure = null;
                try
                {
                    await _client.Request(new Dictionary<string, object> { { "command", "server_info" } });
                }
                catch (Exception error)
                {
                    failure = error;
                }

                Assert.IsNotNull(failure, "The request died with the connection and has to say so.");
                Assert.IsNotInstanceOfType<ConnectionSupersededException>(
                    failure,
                    "No transition took this connection anywhere - it failed. Naming a transition would be inventing one.");
                Assert.IsInstanceOfType<DisconnectedException>(
                    failure,
                    $"A peer that went away is reported by the close path, got: {failure.GetType().Name}.");
            }
            finally
            {
                dropping.Dispose();
            }
        }

        /// <summary>
        /// Starts a client that will keep trying to reach a server that is not there, so a wait
        /// actually parks instead of being answered on entry.
        /// </summary>
        /// <remarks>
        /// An attempt has to be in progress for the wait to reach its parking spot at all: with
        /// nothing running it is answered by <see cref="NotConnectingException"/> straight away,
        /// which is a different test. The connect task is left running on purpose and torn down by
        /// the cleanup.
        /// </remarks>
        private XrplClient StartClientWaitingForever(int port)
        {
            XrplClient client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
            {
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 1000,
                StopAfterMaxAttempts = false,
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(60),
                UseCustomPing = false,
            });

            _ = client.Connect();
            return client;
        }

        /// <summary>
        /// Waiting for a connection that never comes: the timeout is an answer, the caller's own
        /// cancellation is not, and a bad timeout is the caller's mistake.
        /// </summary>
        /// <remarks>
        /// These three are the boundary of the outcome contract and the reason it is not simply
        /// "every failure becomes a value". A timeout is something the caller has to act on and so
        /// it is reported as <see cref="ConnectionWaitOutcome.TimedOut"/>; cancellation through the
        /// caller's own token stays an exception because that is the .NET convention and because
        /// the caller already knows it cancelled; an invalid timeout is a bug at the call site
        /// rather than an outcome of the connection. This is also the code that changed most when
        /// the wait stopped polling, so it is pinned rather than assumed.
        /// </remarks>
        [TestMethod]
        public async Task TestUTimeoutIsAnAnswerAndCancellationIsNot()
        {
            int port = TestUtils.GetFreePort(); // nothing is listening, and nothing will be
            _client = StartClientWaitingForever(port);

            // The wait has to be parked, not answered on entry.
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            Assert.AreEqual(
                ConnectionWaitOutcome.TimedOut,
                await _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromMilliseconds(300)),
                "Not coming back in time is an answer, not a failure.");

            using CancellationTokenSource alreadyCancelled = new CancellationTokenSource();
            alreadyCancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(30), alreadyCancelled.Token));

            using CancellationTokenSource cancelledWhileWaiting = new CancellationTokenSource();
            Task<ConnectionWaitOutcome> waiting =
                _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(30), cancelledWhileWaiting.Token);
            cancelledWhileWaiting.CancelAfter(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
                await _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.Zero));
        }

        /// <summary>
        /// Waiters do not interfere: one timing out and one cancelling leave the third waiting.
        /// </summary>
        /// <remarks>
        /// The wait sleeps on a signal shared by everyone waiting, so a per-waiter deadline must
        /// not touch it: an implementation that cancelled the shared signal to serve its own
        /// timeout would take every other waiter down with it, and one that never re-armed the
        /// signal after completing it would leave the survivors awake and spinning. Both are the
        /// kind of mistake that shows only with more than one waiter, which is why this test has
        /// three.
        /// </remarks>
        [TestMethod]
        public async Task TestUWaitersDoNotTakeEachOtherDown()
        {
            int port = TestUtils.GetFreePort();
            _client = StartClientWaitingForever(port);

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            using CancellationTokenSource cancelling = new CancellationTokenSource();

            Task<ConnectionWaitOutcome> timesOut =
                _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromMilliseconds(400));
            Task<ConnectionWaitOutcome> cancelled =
                _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(30), cancelling.Token);
            Task<ConnectionWaitOutcome> survives =
                _client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(30));

            cancelling.CancelAfter(TimeSpan.FromMilliseconds(200));

            Assert.AreEqual(ConnectionWaitOutcome.TimedOut, await timesOut);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelled);

            Assert.IsFalse(survives.IsCompleted, "The third waiter has no reason to be finished yet.");

            // And it is still a live waiter rather than a spinning one: the server comes up, and it
            // is the connection that ends the wait.
            CreateMockRippled mock = StartMock(port);
            try
            {
                Assert.AreEqual(ConnectionWaitOutcome.Connected, await survives);
            }
            finally
            {
                mock.Stop();
            }
        }

        /// <summary>
        /// A transition a <c>Disconnect()</c> overtook is told the client is down, not that it was
        /// overtaken.
        /// </summary>
        /// <remarks>
        /// The two answers call for opposite reactions, which is why the disconnect branch keeps
        /// reporting a <see cref="NotConnectedException"/> while every other winner reports a
        /// cancellation: after a <c>Disconnect()</c> nothing is coming back on its own, and a
        /// consumer that treated this as "the switch was overtaken, carry on" would be waiting for
        /// a connection nobody is building.
        /// </remarks>
        [TestMethod]
        public async Task TestUASwitchADisconnectOvertookIsToldTheClientIsDown()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            CreateMockRippled second = StartMock(secondPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                int disconnected = 0;
                _client.OnSessionEnded += async (reason, _) =>
                {
                    if (reason == SessionEndReason.ServerChanged && Interlocked.Exchange(ref disconnected, 1) == 0)
                    {
                        await _client.Disconnect();
                    }
                };

                ClientDisconnectedException error = await Assert.ThrowsExactlyAsync<ClientDisconnectedException>(
                    async () => await _client.connection.ChangeServer($"ws://127.0.0.1:{secondPort}"));

                Assert.IsInstanceOfType<NotConnectedException>(error, "catch (NotConnectedException) must keep catching this.");
                Assert.IsFalse(_client.connection.IsConnected(), "The disconnect won, and it has to stay won.");
                _client = null;
            }
            finally
            {
                first.Stop();
                second.Stop();
            }
        }

        /// <summary>
        /// A switch overtaken while it waits for its own connection reports it too.
        /// </summary>
        /// <remarks>
        /// This is the check that runs after the wait rather than before it, and it is reached by a
        /// different route than the others: the switch connected, and only then found the client
        /// somewhere else. Its condition can prove only that the client is not where this call
        /// asked for, so the kind is read from the transition that owns the connection instead of
        /// being assumed from the mismatch - and no existing test in this repository passed through
        /// it at all.
        /// </remarks>
        [TestMethod]
        public async Task TestUASwitchOvertakenWhileWaitingReportsTheWinner()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();
            int thirdPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            CreateMockRippled second = StartMock(secondPort);
            CreateMockRippled third = StartMock(thirdPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                string thirdUrl = $"ws://127.0.0.1:{thirdPort}";
                string secondUrl = $"ws://127.0.0.1:{secondPort}";

                // Issued from the new server's own OnConnected, which runs while the first switch is
                // still inside its wait - so the takeover lands after the connection came up and
                // before the switch checks where it ended up.
                int nested = 0;
                _client.connection.OnConnected += async () =>
                {
                    if (string.Equals(_client.connection.GetUrl(), secondUrl, StringComparison.Ordinal)
                        && Interlocked.Exchange(ref nested, 1) == 0)
                    {
                        await _client.connection.ChangeServer(thirdUrl);
                    }
                };

                ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                    async () => await _client.connection.ChangeServer(secondUrl));

                Assert.AreEqual(thirdUrl, error.SupersededBy, "The caller is told where the client actually is.");
                Assert.AreEqual(ConnectionTransitionKind.ChangeServer, error.Kind);
            }
            finally
            {
                first.Stop();
                second.Stop();
                third.Stop();
            }
        }

        /// <summary>
        /// A <c>Connect()</c> that overtakes a switch is named as a <c>Connect()</c>.
        /// </summary>
        /// <remarks>
        /// The kind is what a consumer reacts to: another <c>ChangeServer</c> put the client on a
        /// server it did not ask for, while a <c>Connect()</c> rebuilt the connection to the one it
        /// was already heading for. Both were the same bare cancellation before.
        /// </remarks>
        [TestMethod]
        public async Task TestUASwitchAConnectOvertookNamesTheConnect()
        {
            int firstPort = TestUtils.GetFreePort();
            int secondPort = TestUtils.GetFreePort();

            CreateMockRippled first = StartMock(firstPort);
            CreateMockRippled second = StartMock(secondPort);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{firstPort}", new XrplClient.ClientOptions
                {
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(10),
                    UseCustomPing = false,
                });

                await _client.Connect();

                int reconnected = 0;
                _client.OnSessionEnded += async (reason, _) =>
                {
                    if (reason == SessionEndReason.ServerChanged && Interlocked.Exchange(ref reconnected, 1) == 0)
                    {
                        await _client.connection.Connect(CancellationToken.None);
                    }
                };

                ConnectionSupersededException error = await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(
                    async () => await _client.connection.ChangeServer($"ws://127.0.0.1:{secondPort}"));

                Assert.AreEqual(ConnectionTransitionKind.Connect, error.Kind);
            }
            finally
            {
                first.Stop();
                second.Stop();
            }
        }

        /// <summary>
        /// A waiter learns that the client gave up as soon as it gives up, not when its own timeout
        /// runs out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This guards the wait itself. While it polled, every terminal state was noticed within a
        /// tick whether or not anything announced it; waiting on a signal instead means a terminal
        /// condition that forgets to wake its waiters is not slow but invisible - the caller sits
        /// out the whole acquisition timeout and is then told it timed out, which is the wrong
        /// answer as well as a late one.
        /// </para>
        /// <para>
        /// The acquisition timeout here is thirty seconds against a reconnect budget that is spent
        /// in well under one, so the assertion can tell "woken by the client" from "gave up
        /// waiting" without depending on how fast the machine is.
        /// </para>
        /// </remarks>
        [TestMethod]
        public async Task TestUAWaiterIsWokenWhenTheClientGivesUpNotWhenItsOwnTimeoutExpires()
        {
            int port = TestUtils.GetFreePort();
            CreateMockRippled mock = StartMock(port);

            try
            {
                _client = new XrplClient($"ws://127.0.0.1:{port}", new XrplClient.ClientOptions
                {
                    ReconnectBaseDelay = TimeSpan.FromMilliseconds(100),
                    ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
                    MaxReconnectAttempts = 2,
                    StopAfterMaxAttempts = true,
                    ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
                    ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
                    UseCustomPing = false,
                });

                await _client.Connect();

                int deadPort = TestUtils.GetFreePort();

                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                await Assert.ThrowsExactlyAsync<ReconnectExhaustedException>(
                    async () => await _client.connection.ChangeServer($"ws://127.0.0.1:{deadPort}"));
                clock.Stop();

                Assert.IsLessThan(
                    TimeSpan.FromSeconds(15),
                    clock.Elapsed,
                    "The waiter has to be woken by the client giving up, not by its own 30s timeout.");
            }
            finally
            {
                mock.Stop();
            }
        }
    }
}
