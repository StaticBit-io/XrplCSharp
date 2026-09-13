using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;

namespace Xrpl.Tests
{
    /// <summary>
    /// <see cref="ConnectionManager"/> as something a consumer can actually call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is reachable from outside the library - <c>client.connection.connectionManager</c> is a
    /// public field on a public type - and the connection notifies it from nine places, on its own
    /// threads. Nothing inside the SDK awaits it, so its defects never showed; that makes them
    /// latent, not absent, and the first consumer to call <c>AwaitConnection()</c> would meet all
    /// of them at once.
    /// </para>
    /// <para>
    /// These are unit tests of the class alone: no socket, no client. What they pin is the
    /// behaviour any waiter is entitled to - resume off the notifying thread, survive a concurrent
    /// notification, and be cancelled rather than faulted.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUConnectionManagerWaiters
    {
        /// <summary>
        /// A waiter resumes after the notification returns, not inside it.
        /// </summary>
        /// <remarks>
        /// The completion sources were created without
        /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>, so every parked waiter
        /// resumed synchronously inside <see cref="ConnectionManager.ResolveAllAwaiting"/> - which
        /// the connection calls from inside <c>OnceOpen</c>, before the <c>OnConnected</c> handler
        /// and while it holds the connection together. Consumer code running there is the defect
        /// issue #177 was about, in a new place.
        /// </remarks>
        [TestMethod]
        public async Task TestUAWaiterDoesNotResumeInsideTheNotification()
        {
            ConnectionManager manager = new ConnectionManager();

            int resumed = 0;
            Task waiter = ResumeMarker();

            async Task ResumeMarker()
            {
                await manager.AwaitConnection();
                Volatile.Write(ref resumed, 1);
            }

            manager.ResolveAllAwaiting();
            int resumedInsideTheCall = Volatile.Read(ref resumed);

            await waiter;

            Assert.AreEqual(
                0,
                resumedInsideTheCall,
                "The waiter resumed inside ResolveAllAwaiting - which the connection calls from inside OnceOpen.");
            Assert.AreEqual(1, Volatile.Read(ref resumed), "It still has to resume, just not there.");
        }

        /// <summary>
        /// A cancelled wait is cancelled, not faulted.
        /// </summary>
        /// <remarks>
        /// <c>SetException(new OperationCanceledException(...))</c> produces a faulted task: the
        /// rule that turns a cancellation into a cancelled task belongs to the async method
        /// builder, not to <see cref="TaskCompletionSource{TResult}"/>. The difference is visible
        /// to anyone reading <c>Task.Status</c>, combining the task with others, or leaving it
        /// unobserved.
        /// </remarks>
        [TestMethod]
        public async Task TestUACancelledWaitIsCancelledRatherThanFaulted()
        {
            ConnectionManager manager = new ConnectionManager();

            Task waiter = manager.AwaitConnection();
            manager.RejectAllAwaitingWithCancellation();

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiter);
            Assert.AreEqual(TaskStatus.Canceled, waiter.Status);
        }

        /// <summary>
        /// Registering while a notification is going out does not corrupt the list or lose a waiter.
        /// </summary>
        /// <remarks>
        /// The waiters lived in a plain <c>List&lt;T&gt;</c> mutated without synchronisation, while
        /// the connection notifies from its socket, reconnect and caller threads. Both failures are
        /// real: an <c>InvalidOperationException</c> from a list modified while being iterated, and
        /// a waiter added in that window and dropped by the reassignment that follows - which is the
        /// worse of the two, because it never comes back and never says anything.
        /// </remarks>
        [TestMethod]
        public async Task TestURegisteringDuringANotificationLosesNobody()
        {
            for (int round = 0; round < 200; round++)
            {
                ConnectionManager manager = new ConnectionManager();
                List<Task> waiters = new List<Task>();

                Task registering = Task.Run(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        lock (waiters)
                        {
                            waiters.Add(manager.AwaitConnection());
                        }
                    }
                });

                Task notifying = Task.Run(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        manager.ResolveAllAwaiting();
                    }
                });

                await Task.WhenAll(registering, notifying);

                // Whatever the interleaving was, everyone still parked is released by this one.
                manager.ResolveAllAwaiting();

                Task all;
                lock (waiters)
                {
                    all = Task.WhenAll(waiters);
                }

                Task finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.AreSame(all, finished, $"A waiter was dropped and never resumed (round {round}).");
                await all;
            }
        }
    }
}
