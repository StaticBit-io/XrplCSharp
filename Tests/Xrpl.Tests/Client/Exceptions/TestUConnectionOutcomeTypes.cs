using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;

namespace Xrpl.Tests.Client.Exceptions
{
    /// <summary>
    /// The types that say what happened to the connection, rather than leaving it in the message
    /// text - see <c>specs/2026-09-09-connection-outcome-api.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="NotConnectedException"/> carried five different events and
    /// <see cref="OperationCanceledException"/> two, so a consumer that had to react differently to
    /// each was left classifying by text - which the release notes of 11.3.2.0 told them not to do,
    /// while the library gave them no type capable of it.
    /// </para>
    /// <para>
    /// These tests are about the types themselves: what they derive from, and what they carry.
    /// Which path produces which type is a question about the connection, and lives in
    /// <c>TestUConnectionOutcomes</c>.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUConnectionOutcomeTypes
    {
        /// <summary>
        /// The failure that caused a refusal survives on the exception that reports it.
        /// </summary>
        /// <remarks>
        /// <see cref="NotConnectedException"/> had a single message constructor, so a subtype that
        /// has a cause - the handler exception behind
        /// <see cref="ConnectHandlerFailedException"/> - had nowhere to put it: there is no setter
        /// for <see cref="Exception.InnerException"/>. The whole taxonomy rests on this one
        /// constructor existing.
        /// </remarks>
        [TestMethod]
        public void TestUNotConnectedExceptionKeepsTheFailureThatCausedIt()
        {
            InvalidOperationException cause = new InvalidOperationException("the handler threw");

            NotConnectedException error = new NotConnectedException("gave up connecting", cause);

            Assert.AreSame(cause, error.InnerException);
            Assert.AreEqual("gave up connecting", error.Message);
        }

        /// <summary>
        /// Every new type is still a <see cref="NotConnectedException"/>.
        /// </summary>
        /// <remarks>
        /// This is what makes the change additive rather than breaking: the existing
        /// <c>catch (NotConnectedException)</c> in consumer code, and the ones inside this library,
        /// keep catching all five events. Only code that wants to tell them apart has to look.
        /// </remarks>
        [TestMethod]
        public void TestUEveryOutcomeIsStillANotConnectedException()
        {
            Assert.IsInstanceOfType<NotConnectedException>(new ClientDisconnectedException("x"));
            Assert.IsInstanceOfType<NotConnectedException>(new ReconnectExhaustedException("x", attempts: 1, maxAttempts: 1));
            Assert.IsInstanceOfType<NotConnectedException>(new RequestRefusedException("x"));
            Assert.IsInstanceOfType<NotConnectedException>(new ConnectHandlerFailedException("x", failures: 1));
            Assert.IsInstanceOfType<NotConnectedException>(new NotConnectingException("x"));
        }

        /// <summary>
        /// Exhaustion says how many attempts were spent and what the budget was.
        /// </summary>
        /// <remarks>
        /// Both numbers are reported, not just the limit, because a consumer deciding whether to
        /// fail over wants to know the budget it actually configured was really spent. The raw
        /// counter in the connection stands at <c>MaxAttempts + 1</c> when the loop stops - it is
        /// incremented at the head of a pass and the loop breaks on the pass that exceeds the
        /// budget - so "6 of 5" is what a naive reading would publish. The throw site passes the
        /// number of attempts made.
        /// </remarks>
        [TestMethod]
        public void TestUExhaustionCarriesTheAttemptsItSpent()
        {
            ReconnectExhaustedException error = new ReconnectExhaustedException(
                "Connection failed permanently after 5 attempts. Reconnection has been stopped.",
                attempts: 5,
                maxAttempts: 5);

            Assert.AreEqual(5, error.Attempts);
            Assert.AreEqual(5, error.MaxAttempts);
        }

        /// <summary>
        /// A failing <c>OnConnected</c> handler reports how often it failed, and with what.
        /// </summary>
        /// <remarks>
        /// This is the one outcome where the node is answering and the fault is on this side, so a
        /// consumer that reacts to it by failing over to another server would be moving away from
        /// a healthy node. The handler's own exception is what says why, and it used to reach the
        /// caller only as text inside the message.
        /// </remarks>
        [TestMethod]
        public void TestUAFailingConnectHandlerCarriesItsCountAndItsCause()
        {
            InvalidOperationException cause = new InvalidOperationException("subscription refused");

            ConnectHandlerFailedException error = new ConnectHandlerFailedException(
                "Gave up connecting: the OnConnected handler failed 3 time(s) in a row.",
                failures: 3,
                innerException: cause);

            Assert.AreEqual(3, error.Failures);
            Assert.AreSame(cause, error.InnerException);
        }

        /// <summary>
        /// Being overtaken is still a cancellation, and now says by what and where it went.
        /// </summary>
        /// <remarks>
        /// The four supersession sites already threw <see cref="OperationCanceledException"/>, so
        /// deriving from it keeps every <c>catch</c> and every task status exactly as they were.
        /// What the caller could not do before is tell "another operation took the connection over"
        /// apart from "my own token was cancelled".
        /// </remarks>
        [TestMethod]
        public void TestUSupersessionIsACancellationThatNamesItsWinner()
        {
            ConnectionSupersededException error = new ConnectionSupersededException(
                "Superseded by a later ChangeServer to wss://example.test:6006.",
                ConnectionTransitionKind.ChangeServer,
                supersededBy: "wss://example.test:6006");

            Assert.IsInstanceOfType<OperationCanceledException>(error, "catch (OperationCanceledException) must keep catching this.");
            Assert.AreEqual(ConnectionTransitionKind.ChangeServer, error.Kind);
            Assert.AreEqual("wss://example.test:6006", error.SupersededBy);
        }

        /// <summary>
        /// It carries no cancellation token, because nobody cancelled anything.
        /// </summary>
        /// <remarks>
        /// A caller that filters its own cancellations with
        /// <c>catch (OperationCanceledException ex) when (ex.CancellationToken == myToken)</c>
        /// must not have that filter match here: the operation was overtaken by another operation,
        /// not cancelled by the caller. This is the reason the constructor does not take a token.
        /// </remarks>
        [TestMethod]
        public void TestUSupersessionCarriesNobodysToken()
        {
            using CancellationTokenSource callerToken = new CancellationTokenSource();

            ConnectionSupersededException error = new ConnectionSupersededException(
                "Superseded by a later Connect().",
                ConnectionTransitionKind.Connect);

            Assert.AreNotEqual(callerToken.Token, error.CancellationToken);
            Assert.AreEqual(CancellationToken.None, error.CancellationToken);
            Assert.IsNull(error.SupersededBy, "A Connect() that won left the client where it already was.");
        }

        /// <summary>
        /// Awaited directly, the subtype reaches the caller - and the task reports itself cancelled.
        /// </summary>
        /// <remarks>
        /// <c>AsyncTaskMethodBuilder</c> turns an escaping <see cref="OperationCanceledException"/>
        /// into a cancelled task whatever its token says, so this is the behaviour the supersession
        /// sites already had; the test pins that inheritance did not change it.
        /// </remarks>
        [TestMethod]
        public async Task TestUSupersessionSurvivesADirectAwait()
        {
            Task direct = SupersededAsync();

            ConnectionSupersededException caught =
                await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(async () => await direct);

            Assert.AreEqual(ConnectionTransitionKind.Reconnect, caught.Kind);
            Assert.AreEqual(
                TaskStatus.Canceled,
                direct.Status,
                "An OperationCanceledException leaving an async method cancels its task.");
        }

        /// <summary>
        /// Through <c>Task.WhenAll</c> the subtype survives too, as long as nothing faulted.
        /// </summary>
        /// <remarks>
        /// Measured rather than assumed, and the measurement contradicted the expectation this test
        /// was written from: <c>WhenAll</c> with cancellations and no faults stores the first
        /// cancellation exception and rethrows that very instance, so
        /// <see cref="ConnectionSupersededException.Kind"/> is still readable. Documenting the
        /// pessimistic rule would have sent consumers looking for a workaround they do not need.
        /// </remarks>
        [TestMethod]
        public async Task TestUSupersessionSurvivesWhenAllWithNoFaults()
        {
            Task combined = Task.WhenAll(SupersededAsync(), Task.CompletedTask);

            ConnectionSupersededException caught =
                await Assert.ThrowsExactlyAsync<ConnectionSupersededException>(async () => await combined);

            Assert.AreEqual(ConnectionTransitionKind.Reconnect, caught.Kind);
        }

        /// <summary>
        /// A fault alongside it loses the supersession entirely.
        /// </summary>
        /// <remarks>
        /// <c>WhenAll</c> prefers faults to cancellations: with any faulted task the combined task
        /// faults, and - measured here rather than assumed - the cancellation is not recorded at
        /// all, so it is absent from <c>Task.Exception.InnerExceptions</c> as well as from the
        /// <c>await</c>. This is the one case where a caller cannot learn it was overtaken, and it
        /// is why the XML documentation of the type says to await the operation itself.
        /// </remarks>
        [TestMethod]
        public async Task TestUAFaultAlongsideItLosesTheSupersession()
        {
            static async Task FaultedAsync()
            {
                await Task.Yield();
                throw new InvalidOperationException("something else went wrong");
            }

            Task combined = Task.WhenAll(SupersededAsync(), FaultedAsync());

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await combined);

            Assert.AreEqual(TaskStatus.Faulted, combined.Status);
            Assert.IsFalse(
                combined.Exception!.InnerExceptions.Any(e => e is ConnectionSupersededException),
                "WhenAll records faults only: a cancellation alongside a fault is dropped, not merely hidden.");
        }

        private static async Task SupersededAsync()
        {
            await Task.Yield();
            throw new ConnectionSupersededException("overtaken", ConnectionTransitionKind.Reconnect);
        }
    }
}
