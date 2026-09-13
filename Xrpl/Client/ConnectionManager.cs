using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/client/ConnectionManager.ts

namespace Xrpl.Client
{
    /// <summary>
    /// Holds the callers waiting for the connection to come up, and releases them when it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The connection notifies this from its socket, reconnect and caller threads, while consumers
    /// register from theirs: <c>client.connection.connectionManager</c> is reachable from outside
    /// the library. The list is therefore shared state and is guarded as such - it was not, and a
    /// registration landing inside a notification threw
    /// <see cref="InvalidOperationException"/> ("Collection was modified") or, worse, was dropped
    /// by the reassignment that followed and never resumed.
    /// </para>
    /// <para>
    /// Waiters are released outside the lock and resume asynchronously. Both matter for the same
    /// reason: <see cref="ResolveAllAwaiting"/> is called from inside the connection's own critical
    /// path, before the <c>OnConnected</c> handler, and a waiter resuming there would run consumer
    /// code in the middle of a connection being assembled.
    /// </para>
    /// </remarks>
    public class ConnectionManager
    {
        private readonly object _waitersLock = new object();

        private List<TaskCompletionSource<object>> _promisesAwaitingConnection = new List<TaskCompletionSource<object>>();

        /// <summary>
        /// Takes the waiters out, leaving an empty list behind for anyone registering next.
        /// </summary>
        private List<TaskCompletionSource<object>> TakeWaiters()
        {
            lock (_waitersLock)
            {
                List<TaskCompletionSource<object>> waiting = _promisesAwaitingConnection;
                _promisesAwaitingConnection = new List<TaskCompletionSource<object>>();
                return waiting;
            }
        }

        /// <summary>Releases everyone waiting: the connection is up.</summary>
        public void ResolveAllAwaiting()
        {
            foreach (TaskCompletionSource<object> waiter in TakeWaiters())
            {
                waiter.TrySetResult(null);
            }
        }

        /// <summary>Fails everyone waiting with <paramref name="error"/>.</summary>
        public void RejectAllAwaiting(Exception error)
        {
            foreach (TaskCompletionSource<object> waiter in TakeWaiters())
            {
                waiter.TrySetException(error);
            }
        }

        /// <summary>
        /// Cancels everyone waiting, for a connection that was closed on purpose.
        /// </summary>
        /// <remarks>
        /// <c>TrySetCanceled</c> rather than an <see cref="OperationCanceledException"/> handed to
        /// <c>TrySetException</c>: the second produces a faulted task, and the rule that turns a
        /// cancellation into a cancelled task belongs to the async method builder rather than to
        /// <see cref="TaskCompletionSource{TResult}"/>.
        /// </remarks>
        public void RejectAllAwaitingWithCancellation()
        {
            foreach (TaskCompletionSource<object> waiter in TakeWaiters())
            {
                waiter.TrySetCanceled();
            }
        }

        /// <summary>Waits until the connection is up, or until it is given up on.</summary>
        public async Task AwaitConnection()
        {
            // RunContinuationsAsynchronously is not optional here - see the note on the class.
            TaskCompletionSource<object> waiter =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_waitersLock)
            {
                _promisesAwaitingConnection.Add(waiter);
            }

            await waiter.Task;
        }
    }
}
