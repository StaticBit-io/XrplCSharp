using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.Client
{
    /// <summary>
    /// Represents an isolated connection session with its own WebSocketClient and state.
    /// Used for per-session isolation during ChangeServer operations.
    /// 
    /// Each session has:
    /// - Unique session ID for callback validation
    /// - Own WebSocketClient instance
    /// - Own intentional disconnect flag (not shared with other sessions)
    /// - Completion TaskCompletionSource that signals when OnDisconnected has run
    /// 
    /// This prevents race conditions where old session callbacks affect new session state.
    /// </summary>
    internal class ConnectionSession
    {
        private static long _sessionIdCounter = 0;
        
        public long SessionId { get; }
        public WebSocketClient? Socket { get; private set; }
        public bool IsIntentionalDisconnect { get; set; }
        public bool IsRetiring { get; private set; }
        
        private readonly TaskCompletionSource<bool> _completionTcs;
        
        /// <summary>
        /// Task that completes when this session's OnDisconnected callback has finished.
        /// </summary>
        public Task Completion => _completionTcs.Task;
        
        public ConnectionSession(WebSocketClient socket)
        {
            SessionId = Interlocked.Increment(ref _sessionIdCounter);
            Socket = socket;
            IsIntentionalDisconnect = false;
            IsRetiring = false;
            _completionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        
        /// <summary>
        /// Marks this session as retiring. Callbacks from retiring sessions are ignored.
        /// </summary>
        public void MarkAsRetiring()
        {
            IsRetiring = true;
            IsIntentionalDisconnect = true;
            Socket?.SetIntentionalDisconnect();
        }
        
        /// <summary>
        /// Signals that this session has completed its cleanup.
        /// Called after OnDisconnected has run.
        /// </summary>
        public void CompleteSession()
        {
            _completionTcs.TrySetResult(true);
        }
        
        /// <summary>
        /// Initiates graceful close of this session's socket.
        /// Does not block - cleanup happens asynchronously.
        /// </summary>
        public async Task GracefulCloseAsync()
        {
            var socket = Socket;
            if (socket == null)
            {
                CompleteSession();
                return;
            }
            
            try
            {
                await socket.InitiateGracefulCloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallow exceptions - this is background cleanup
            }
            finally
            {
                // Session completion is signaled by OnceClose callback, not here
                // (unless socket was null)
            }
        }
        
        /// <summary>
        /// Clears the socket reference. Call after session cleanup.
        /// </summary>
        public void ClearSocket()
        {
            Socket = null;
        }
    }
}
