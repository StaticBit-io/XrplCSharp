using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;

using static Xrpl.Client.RequestManager;

using Xrpl.AddressCodec;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Methods;

using Timer = System.Timers.Timer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/client/connection.ts

namespace Xrpl.Client;

public enum ConnectionCloseSeverity
{
    Info,

    Warning,

    Error,
}

public enum RequestFailurePolicy
{
    ImmediateFail,

    WaitForConnection,
}

public enum XrpConnectionState
{
    Disconnected,

    Connecting,

    Connected,

    RestoringConnection,
}

public class ReconnectInfo
{
    public int CurrentAttempt { get; set; }

    public int MaxAttempts { get; set; }

    public TimeSpan RemainingDelay { get; set; }
}

public class ConnectionStatusInfo
{
    public string Message { get; set; }

    public ConnectionCloseSeverity Severity { get; set; }

    public ReconnectInfo? Reconnect { get; set; }

    public XrpConnectionState ConnectionState { get; set; }
}

public class Connection
{
    public event OnError OnError;

    public event OnWarning OnWarning;

    public event OnServerWarning OnServerWarning;

    public event OnConnected OnConnected;

    public event OnDisconnect OnDisconnect;

    public event OnPing OnPing;

    public event OnLedgerClosed OnLedgerClosed;

    public event OnTransaction OnTransaction;

    public event OnManifestReceived OnManifestReceived;

    public event OnPeerStatusChange OnPeerStatusChange;

    public event OnConsensusPhase OnConsensusPhase;

    public event OnPathFind OnPathFind;

    public event Action<ConnectionStatusInfo> OnConnectionStatus;

    public static string Base64Encode(string plainText)
    {
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(plainTextBytes);
    }

    public static string Base64Decode(string base64EncodedData)
    {
        var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
        return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
    }

    public class Trace
    {
        public string id { get; set; }

        public string message { get; set; }
    }

    public class ConnectionOptions
    {
        public Trace trace { get; set; }

        public string proxy { get; set; }

        public string proxyAuthorization { get; set; }

        public string authorization { get; set; }

        public string trustedCertificates { get; set; }

        public string key { get; set; }

        public string passphrase { get; set; }

        public string certificate { get; set; }

        public Dictionary<string, dynamic> headers { get; set; }

        /// <summary>
        /// Timeout for individual API requests after connection is established.
        /// This controls how long to wait for a response to a single request (e.g., account_info, submit).
        /// Default: 40 seconds.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(40);

        /// <summary>
        /// Timeout for a single WebSocket connection attempt.
        /// If the connection cannot be established within this time, it will fail and trigger reconnection logic.
        /// Should be shorter than ConnectionAcquisitionTimeout to allow multiple retry attempts.
        /// Default: 30 seconds.
        /// </summary>
        public TimeSpan ConnectionAttemptTimeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Gets or sets the base delay interval used between automatic reconnection attempts.
        /// </summary>
        public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Gets or sets the maximum delay between automatic reconnection attempts after a disconnection.
        /// </summary>
        /// <remarks>This value determines the upper bound for the time interval between
        /// reconnection attempts. If the connection is lost, the delay between retries will not exceed this value,
        /// even if a backoff strategy is used.</remarks>
        public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum number of times the system will attempt to reconnect after a disconnection.
        /// </summary>
        /// <remarks>Set this property to limit how many reconnection attempts are made before
        /// giving up. A value of 0 disables automatic reconnection.</remarks>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// Gets or sets a value indicating whether the operation should stop after reaching the maximum number of
        /// attempts.
        /// </summary>
        /// <remarks>Set this property to <see langword="true"/> to prevent further retries once
        /// the maximum attempt count is reached. If set to <see langword="false"/>, the operation may continue
        /// beyond the maximum attempts, depending on the retry policy.</remarks>
        public bool StopAfterMaxAttempts { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to use a custom ping<br/>
        /// implementation instead of the default behavior.
        /// </summary>
        public bool UseCustomPing { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to enable periodic background health monitoring of the WebSocket connection.<br/>
        /// When enabled, the connection state is checked every 20 seconds. If the WebSocket is detected as Closed or Aborted,
        /// or if no data has been received for more than 60 seconds, an automatic reconnection is triggered.<br/>
        /// This check does not send any network requests — it only inspects the local connection state.<br/>
        /// Automatically enabled when <see cref="UseCustomPing"/> is set to <see langword="true"/>.<br/>
        /// Default: <see langword="false"/>.
        /// </summary>
        public bool UseCheckHealth { get; set; } = false;

        /// <summary>
        /// Gets or sets the policy that determines how failed requests are handled.
        /// </summary>
        /// <remarks>
        /// Use this property to specify the strategy for handling request failures,<br/>
        /// such as whether to retry, delay, or fail immediately.<br/>
        /// The selected policy affects how the system responds to transient errors or network issues.</remarks>
        public RequestFailurePolicy RequestPolicy { get; set; } = RequestFailurePolicy.WaitForConnection;

        /// <summary>
        /// Maximum time to wait for connection when using WaitForConnection request policy.
        /// This is the total time allowed for multiple connection attempts, including retry delays.
        /// Must be >= ConnectionAttemptTimeout to allow at least one full connection attempt.
        /// Default: 30 seconds.
        /// </summary>
        public TimeSpan ConnectionAcquisitionTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    private void ValidateConfig()
    {
        if (config.ConnectionAcquisitionTimeout < config.ConnectionAttemptTimeout)
        {
            throw new ArgumentException(
                $"ConnectionAcquisitionTimeout ({config.ConnectionAcquisitionTimeout.TotalSeconds}s) must be >= ConnectionAttemptTimeout ({config.ConnectionAttemptTimeout.TotalSeconds}s) to allow at least one full connection attempt.");
        }
    }

    private static WebSocketClient CreateWebSocket(string url, ConnectionOptions config) =>

        // Client or Creation...
        //ClientWebSocketOptions options = new ClientWebSocketOptions()
        //{
        //    Proxy = config.proxy,
        //    Credentials = config.authorization,
        //    ClientCertificates = config.trustedCertificates
        //};
        //options.agent = getAgent(url, config)
        //WebSocketCreationOptions create = new WebSocketCreationOptions()
        //{
        //};
        //  if (config.authorization != null)
        //  {
        //      string base64 = Base64Encode(config.authorization);
        //      options.headers = {
        //          ...options.headers,
        //          Authorization: $"Basic {base64}",
        //      }
        //      const optionsOverrides = _.omitBy(
        //      {
        //          ca: config.trustedCertificates,
        //          key: config.key,
        //          passphrase: config.passphrase,
        //          cert: config.certificate,
        //  },
        //  (value) => value == null,
        //)
        //const websocketOptions = { ...options, ...optionsOverrides };
        WebSocketClient.Create(url); // todo add options

    public string url { get; private set; }

    public WebSocketClient ws;

    private int? reconnectTimeoutID = null;

    private int? heartbeatIntervalID = null;

    private int _reconnectAttempts = 0;

    private static readonly Random _random = new();

    private CancellationTokenSource _reconnectCts;

    private Task _reconnectLoop;

    private SemaphoreSlim _connectLock = new(initialCount: 1, maxCount: 1);

    private DateTime? lastActivityTime = null;

    private Timer? pingTimer = null;

    private CancellationTokenSource? _pingCts = null;

    private Task? _currentPingTask = null;

    private WebSocketClient? _userInitiatedSocket = null;

    private WebSocketClient? _lastActiveSocket = null;

    private readonly HashSet<WebSocketClient> _userInitiatedSockets = new();

    private readonly object _userInitiatedSocketsLock = new();

    private volatile bool _permanentlyDisconnected = false;

    private volatile bool _isIntentionalDisconnect = false;

    // Socket that was closed due to ping timeout - late callbacks from this socket should be ignored
    private volatile WebSocketClient? _pingTimeoutSocket = null;

    // Socket that was closed due to network drop - late callbacks from this socket should be ignored
    private volatile WebSocketClient? _networkDropSocket = null;

    // Fast-path message processing: prioritize request responses (including ping/pong) over stream data
    // to prevent head-of-line blocking that causes ping timeouts under high stream load
    // Channel is created per-session to prevent cross-session message leakage
    // Using Channel<T> instead of BlockingCollection for true async support in WebAssembly
    private Channel<string>? _streamMessageChannel = null;
    private CancellationTokenSource? _messageProcessorCts = null;
    private Task? _messageProcessorTask = null;
    private readonly object _messageProcessorLock = new();

    // Reconnect mode enum for reliable state tracking across all reconnect paths
    private enum ReconnectMode { None, FastReconnect, LoopReconnect }
    
    // Current reconnect mode - set before any reconnect attempt, cleared only when connection stable
    // This ensures all callbacks see the correct reconnect state regardless of timing
    private volatile ReconnectMode _reconnectMode = ReconnectMode.None;
    
    // Legacy flag for backward compatibility (kept for any external checks)
    private volatile bool _isFastReconnectActive = false;

    private volatile XrpConnectionState _currentConnectionState = XrpConnectionState.Disconnected;

    private TaskCompletionSource<bool>? _disconnectTcs = null;

    private readonly object _disconnectLock = new();

    // Per-session isolation for ChangeServer
    private ConnectionSession? _activeSession = null;

    private readonly object _sessionLock = new();

    public XrpConnectionState CurrentConnectionState => _currentConnectionState;

    private XrpConnectionState _previousNotifiedState = XrpConnectionState.Disconnected;

    private string _previousNotifiedMessage = string.Empty;

    private void SetConnectionState(
        XrpConnectionState newState,
        string message,
        ConnectionCloseSeverity severity = ConnectionCloseSeverity.Info,
        ReconnectInfo? reconnect = null)
    {
        var previousState = _currentConnectionState;
        _currentConnectionState = newState;

        var stateChanged = previousState != newState;
        var hasReconnectInfo = reconnect != null;
        var messageChanged = _previousNotifiedMessage != message;
        var isRestoringConnection = newState == XrpConnectionState.RestoringConnection;

        if (!stateChanged && !hasReconnectInfo && !(isRestoringConnection && messageChanged))
        {
            return;
        }

        _previousNotifiedState = newState;
        _previousNotifiedMessage = message;

        OnConnectionStatus?.Invoke(
            new ConnectionStatusInfo
            {
                Message = message,
                Severity = severity,
                Reconnect = reconnect,
                ConnectionState = newState,
            });
    }

    private ReconnectInfo BuildReconnectInfo(int? explicitAttempt = null, TimeSpan? delay = null)
    {
        var attempt = explicitAttempt ?? _reconnectAttempts;
        if (attempt < 1) attempt = 1;
        return new ReconnectInfo
        {
            CurrentAttempt = attempt,
            MaxAttempts = config.MaxReconnectAttempts,
            RemainingDelay = delay ?? TimeSpan.Zero,
        };
    }
    
    private bool IsReconnectActive()
    {
        // Reconnect mode is the authoritative source of truth
        // It's set before any reconnect starts and cleared only when connection is stable
        return _reconnectMode != ReconnectMode.None;
    }

    public ConnectionOptions config { get; private set; }

    public RequestManager requestManager = new();

    public ConnectionManager connectionManager = new();

    public Connection(string server, ConnectionOptions? options = null)
    {
        url = server;
        config = options ?? new ConnectionOptions();

        ValidateConfig();
    }

    public async Task ChangeServer(
        string server,
        ConnectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SetConnectionState(XrpConnectionState.Connecting, message: $"ChangeServer: Switching to {server}...");

        // =====================================================
        // FAST CHANGE SERVER with PER-SESSION ISOLATION
        // =====================================================
        // Old session is marked as retiring and cleaned up in background.
        // New session is created immediately without waiting.
        // Callbacks check session ID to ignore retiring sessions.

        // 1. Quick state cleanup - stop reconnect loop
        StopReconnectLoop();
        
        // 2. Cancel ping timer (but don't wait yet)
        StopPingTimerSync();
        
        // 3. Reject all pending requests BEFORE waiting for ping
        // This allows the ping handler to receive OperationCanceledException and exit quickly
        requestManager.RejectAllWithCancellation();
        connectionManager.RejectAllAwaitingWithCancellation();
        
        // 4. Now wait for ping to finish (should be very fast since requests were rejected)
        await WaitForPingToFinishAsync();

        // 5. Mark old session as retiring (callbacks will be ignored)
        ConnectionSession? oldSession;
        lock (_sessionLock)
        {
            oldSession = _activeSession;
            oldSession?.MarkAsRetiring();
        }

        // 6. Capture old socket and clear ws reference
        WebSocketClient? oldSocket;
        lock (_disconnectLock)
        {
            oldSocket = ws;
            ws = null;
        }

        // 7. Mark socket for intentional disconnect
        if (oldSocket != null)
        {
            _isIntentionalDisconnect = true;
            Interlocked.Exchange(ref _userInitiatedSocket, oldSocket);
            MarkSocketAsUserInitiated(oldSocket);

            // 6. Fire-and-forget GRACEFUL disposal - no blocking
            _ = RetireOldSessionAsync(oldSession, oldSocket);
        }

        // 7. Update config for new server
        url = server;
        if (options != null)
        {
            config = options;
        }

        ValidateConfig();
        _reconnectAttempts = 0;

        // 8. Reset permanentlyDisconnected for new connection
        _permanentlyDisconnected = false;

        // _isIntentionalDisconnect stays true - reset in OnceOpen

        // 9. Immediately connect to new server (new session created in Connect)
        await Connect(cancellationToken);
    }

    /// <summary>
    /// Retires an old session in background. Does not block ChangeServer.
    /// </summary>
    private async Task RetireOldSessionAsync(ConnectionSession? session, WebSocketClient oldSocket)
    {
        try
        {
            oldSocket.SetIntentionalDisconnect();
            await oldSocket.InitiateGracefulCloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // Swallow - fire-and-forget cleanup
        }
        finally
        {
            session?.CompleteSession();
        }
    }

    /// <summary>
    /// Retires current session and reconnects immediately (same flow as ChangeServer).
    /// Used for ping timeout and network drop to avoid slow reconnect with exponential backoff.
    /// </summary>
    private async Task RetireCurrentSessionAndReconnectAsync(string reason)
    {
        // =====================================================
        // CRITICAL: Set reconnect state FIRST so IsReconnectActive() returns true
        // throughout the entire operation, including if Connect() fails.
        // =====================================================
        
        // 1. Set reconnect mode FIRST - this is the authoritative state
        // It will be cleared only when connection is stable (in OnceOpen)
        _reconnectMode = ReconnectMode.FastReconnect;
        _isFastReconnectActive = true; // Keep for backward compatibility
        
        // 2. Stop any existing reconnect loop
        var oldCts = _reconnectCts;
        oldCts?.Cancel();
        oldCts?.Dispose();
        _reconnectLoop = null; // Clear old loop reference so StartReconnectLoop can start a new one
        
        // 3. Initialize reconnect state BEFORE any notifications
        _reconnectAttempts = 1;
        _reconnectCts = new CancellationTokenSource();
        
        // 4. Now send first notification - IsReconnectActive() will return true
        SetConnectionState(
            XrpConnectionState.RestoringConnection,
            message: $"{reason} Reconnecting immediately...",
            ConnectionCloseSeverity.Warning,
            reconnect: BuildReconnectInfo());

        // =====================================================
        // FAST RECONNECT with PER-SESSION ISOLATION (same as ChangeServer)
        // =====================================================
        // Old session is marked as retiring and cleaned up in background.
        // New session is created immediately without waiting.
        // Callbacks check session ID to ignore retiring sessions.

        // 4. Stop ping timer (but don't wait yet)
        StopPingTimerSync();
        
        // 5. Reject all pending requests BEFORE waiting for ping
        // This allows the ping handler to receive OperationCanceledException and exit quickly
        requestManager.RejectAllWithCancellation();
        connectionManager.RejectAllAwaitingWithCancellation();
        
        // 6. Now wait for ping to finish (should be very fast since requests were rejected)
        await WaitForPingToFinishAsync().ConfigureAwait(false);

        // 7. Mark old session as retiring (callbacks will be ignored)
        ConnectionSession? oldSession;
        lock (_sessionLock)
        {
            oldSession = _activeSession;
            oldSession?.MarkAsRetiring();
        }

        // 8. Capture old socket and clear ws reference
        WebSocketClient? oldSocket;
        lock (_disconnectLock)
        {
            oldSocket = ws;
            ws = null;
        }

        // 9. Mark old socket for intentional disconnect (per-socket tracking only)
        // CRITICAL: Do NOT set global _isIntentionalDisconnect = true for ping/network recoveries!
        // The global flag would block OnConnectionFailed from processing new connection failures.
        // Instead, rely solely on per-socket tracking (_userInitiatedSockets HashSet) to filter
        // late callbacks from the old socket while keeping global state clean for the new connection.
        if (oldSocket != null)
        {
            // Per-socket tracking - filters late callbacks from this specific socket
            Interlocked.Exchange(ref _userInitiatedSocket, oldSocket);
            MarkSocketAsUserInitiated(oldSocket);
            oldSocket.SetIntentionalDisconnect(); // Suppresses Critical logging in receive loop

            // 9. Fire-and-forget GRACEFUL disposal - no blocking
            _ = RetireOldSessionAsync(oldSession, oldSocket);
        }

        // 10. Clear ping/network drop socket tracking (old socket is retired)
        // CRITICAL: If not cleared, these stale references would cause OnConnectionFailed
        // to filter callbacks from the NEW socket if Connect() fails, blocking reconnection.
        _pingTimeoutSocket = null;
        _networkDropSocket = null;

        // 11. Reset permanentlyDisconnected for new connection
        _permanentlyDisconnected = false;

        // Note: _reconnectAttempts and _reconnectCts already set at the start of this method
        // Global _isIntentionalDisconnect stays false - allows new connection failures to be processed

        // 12. Immediately connect (bypass Connect() which calls StopReconnectLoop)
        // Note: Don't emit Connecting state here - we already emitted RestoringConnection
        // and Connecting would overwrite ReconnectInfo, confusing consuming apps
        try
        {
            await ConnectInternalAsync().ConfigureAwait(false);
            await WaitForConnectionAsync(config.ConnectionAcquisitionTimeout, CancellationToken.None).ConfigureAwait(false);
            
            // Connect succeeded - cleanup reconnect state
            // Note: _reconnectMode will be cleared in OnceOpen when connection is fully established
            _isFastReconnectActive = false;
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            _reconnectAttempts = 0;
        }
        catch (Exception ex)
        {
            // If Connect fails, transition to loop reconnect mode
            // Keep _reconnectMode set (will be LoopReconnect after StartReconnectLoop)
            
            // _reconnectCts is already set, so StartReconnectLoop will reuse it
            SetConnectionState(
                XrpConnectionState.RestoringConnection,
                message: $"Reconnection failed: {ex.Message}. Retrying...",
                ConnectionCloseSeverity.Warning,
                reconnect: BuildReconnectInfo());
            StartReconnectLoop();
        }
    }

    public bool IsConnected() => State() == WebSocketState.Open;

    public async Task WaitForConnectionAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (IsConnected())
        {
            return;
        }

        CheckIfNotConnected();

        var waitTimeout = timeout ?? config.ConnectionAcquisitionTimeout;

        if (waitTimeout != Timeout.InfiniteTimeSpan && waitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(timeout),
                message:
                $"Timeout must be positive or Timeout.InfiniteTimeSpan, but was {waitTimeout.TotalSeconds:F1}s");
        }

        var startTime = DateTime.UtcNow;
        var checkInterval = TimeSpan.FromMilliseconds(100);
        var hasTimeout = waitTimeout != Timeout.InfiniteTimeSpan;

        while (!IsConnected())
        {
            if (config.StopAfterMaxAttempts &&
                _reconnectAttempts >= config.MaxReconnectAttempts &&
                _reconnectCts == null)
            {
                throw new NotConnectedException(
                    $"Connection failed permanently after {config.MaxReconnectAttempts} attempts. " +
                    "Reconnection has been stopped.");
            }

            if (hasTimeout && DateTime.UtcNow - startTime > waitTimeout)
            {
                throw new System.TimeoutException(
                    $"Connection was not established within {waitTimeout.TotalSeconds:F1} seconds");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(message: "Connection wait was cancelled", cancellationToken);
            }

            try
            {
                await Task.Delay(checkInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException(message: "Connection wait was cancelled", cancellationToken);
            }
        }
    }

    public async Task<bool> HasConnectionAsync(TimeSpan? timeout = null)
    {
        try
        {
            await WaitForConnectionAsync(timeout, CancellationToken.None);
            return true;
        }
        catch (System.TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public Timer timer;

    public async Task Connect(CancellationToken cancellationToken)
    {
        if (IsConnected())
        {
            SetConnectionState(XrpConnectionState.Connected, message: $"Already connected to {url}");
            return;
        }

        StopReconnectLoop();
        SetConnectionState(XrpConnectionState.Connecting, message: $"Connecting to {url}...");
        await ConnectInternalAsync();
        await WaitForConnectionAsync(config.ConnectionAcquisitionTimeout, cancellationToken);
    }

    private async Task ConnectInternalAsync(CancellationToken ct = default)
    {
        _permanentlyDisconnected = false;
        await _connectLock.WaitAsync(ct);
        try
        {
            // Check cancellation before proceeding
            ct.ThrowIfCancellationRequested();
            
            if (IsConnected())
            {
                return;
            }

            if (State() == WebSocketState.Connecting)
            {
                await connectionManager.AwaitConnection();
                return;
            }

            if (url == null)
            {
                throw new ConnectionException("Cannot connect because no server was specified");
            }

            if (this.ws != null)
            {
                throw new XrplException("Websocket connection never cleaned up.");
            }

            // Check cancellation again before creating WebSocket
            ct.ThrowIfCancellationRequested();
            
            this.ws = CreateWebSocket(url, config);
            _lastActiveSocket = this.ws;
            var capturedSocket = this.ws;

            // Check cancellation AFTER creating WebSocket - if cancelled, close the socket and exit
            if (ct.IsCancellationRequested)
            {
                try
                {
                    capturedSocket?.SetIntentionalDisconnect();
                    _ = capturedSocket?.InitiateGracefulCloseAsync();
                }
                catch { /* swallow */ }
                finally
                {
                    this.ws = null;
                }
                ct.ThrowIfCancellationRequested();
            }

            // Create session for this connection
            var newSession = new ConnectionSession(this.ws);
            lock (_sessionLock)
            {
                _activeSession = newSession;
            }

            var capturedSession = newSession;

            timer = new Timer(config.ConnectionAttemptTimeout.TotalMilliseconds);
            timer.Elapsed += async (sender, e) =>
            {
                try
                {
                    await OnConnectionFailed(
                        error: new ConnectionException(
                            $"Error: connect() timed out after {config.ConnectionAttemptTimeout.TotalSeconds:F1} seconds. If your internet connection is working, the rippled server may be blocked or inaccessible. You can also try setting the 'ConnectionAttemptTimeout' option in the Client constructor."),
                        capturedSocket,
                        capturedSession.SessionId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now}Connection timer error: {ex.Message}");
                }
            };
            timer.Start();
            if (this.ws == null)
            {
                throw new XrplException("Connect: created null websocket");
            }

            ws.OnConnect(async (connectedSocket) =>
            {
                try
                {
                    await OnceOpen(connectedSocket, capturedSession.SessionId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now}OnConnect callback error: {ex.Message}");
                }
            });

            var capturedTimer = timer;
            ws.OnConnectionError(async (e, errorSocket) =>
            {
                try
                {
                    // Only stop timer if this is the socket that owns it
                    if (errorSocket == capturedSocket)
                    {
                        capturedTimer?.Stop();
                    }

                    await OnConnectionFailed(e, errorSocket, capturedSession.SessionId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now}OnConnectionError callback error: {ex.Message}");
                }
            });

            ws.OnMessageReceived(async (m, ws) =>
            {
                try
                {
                    // Use fast-path processing to prioritize ping/pong responses
                    // and prevent head-of-line blocking from high-volume stream data
                    await IOnMessageFastPath(m);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now}OnMessageReceived callback error: {ex.Message}");
                }
            });
            ws.OnDisconnect(async (closeStatus, closeDescription, closingSocket) =>
            {
                try
                {
                    // Only stop timer if this is the socket that owns it
                    if (closingSocket == capturedSocket)
                    {
                        capturedTimer?.Stop();
                    }

                    var code = (int?)closeStatus;
                    await OnceClose(code, closeDescription, closingSocket, capturedSession.SessionId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now}OnDisconnect callback error: {ex.Message}");
                }
            });

            await this.ws.Connect();

            connectionManager.AwaitConnection();
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<int> Disconnect()
    {
        _isIntentionalDisconnect = true;
        _permanentlyDisconnected = true;

        var currentSocket = ws;
        if (currentSocket != null)
        {
            MarkSocketAsUserInitiated(currentSocket);
            currentSocket.SetIntentionalDisconnect();
        }

        ClearReconnectState(); // Clear all reconnect state on user disconnect
        StopPingTimerSync();
        
        // Reject pending requests so ping handler can exit quickly
        requestManager.RejectAllWithCancellation();
        connectionManager.RejectAllAwaitingWithCancellation();
        
        await WaitForPingToFinishAsync();

        WebSocketClient? socketToClose;
        lock (_disconnectLock)
        {
            socketToClose = ws;
            ws = null;

            if (socketToClose == null)
            {
                SetConnectionState(XrpConnectionState.Disconnected, message: "Already disconnected.");
                return 0;
            }

            MarkSocketAsUserInitiated(socketToClose);
            socketToClose.SetIntentionalDisconnect();

            if (_disconnectTcs == null || _disconnectTcs.Task.IsCompleted)
            {
                _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        Interlocked.Exchange(ref _userInitiatedSocket, socketToClose);
        CloseSocketIntentionally(socketToClose);

        SetConnectionState(XrpConnectionState.Disconnected, message: "Disconnected by user request.");

        return 0;
    }

    /// <summary>
    /// Disconnects and waits for the WebSocket to be fully closed and cleaned up.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for cleanup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DisconnectAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _isIntentionalDisconnect = true;
        _permanentlyDisconnected = true;

        var currentSocket = ws;
        if (currentSocket != null)
        {
            MarkSocketAsUserInitiated(currentSocket);
            currentSocket.SetIntentionalDisconnect();
        }

        ClearReconnectState(); // Clear all reconnect state on user disconnect
        StopPingTimerSync();
        
        // Reject pending requests so ping handler can exit quickly
        requestManager.RejectAllWithCancellation();
        connectionManager.RejectAllAwaitingWithCancellation();
        
        await WaitForPingToFinishAsync();

        TaskCompletionSource<bool> tcs;
        WebSocketClient? socketToClose;

        lock (_disconnectLock)
        {
            socketToClose = ws;
            ws = null;

            if (socketToClose == null)
            {
                SetConnectionState(XrpConnectionState.Disconnected, message: "Already disconnected.");
                return;
            }

            MarkSocketAsUserInitiated(socketToClose);
            socketToClose.SetIntentionalDisconnect();

            if (_disconnectTcs == null || _disconnectTcs.Task.IsCompleted)
            {
                _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            tcs = _disconnectTcs;
        }

        Interlocked.Exchange(ref _userInitiatedSocket, socketToClose);

        SetConnectionState(XrpConnectionState.Disconnected, message: "Disconnected by user request.");

        // Start disconnect async - it waits for receive loop which calls OnceClose
        // OnceClose will complete tcs, so both should complete around the same time
        var disconnectTask = CloseSocketIntentionallyAsync(socketToClose);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            // Wait for disconnectTask to complete (or timeout)
            // disconnectTask awaits receive loop, which awaits CallOnDisconnectedAsync(OnceClose)
            // OnceClose calls CompleteDisconnectTcs(), so tcs is completed before disconnectTask finishes
            var timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

            // Wait for disconnectTask or timeout
            var completedTask = await Task.WhenAny(disconnectTask, timeoutTask);

            if (completedTask != disconnectTask)
            {
                // Timeout - force complete TCS
                CompleteDisconnectTcs();
            }

            // If disconnectTask completed, OnceClose already called CompleteDisconnectTcs
            // No need to call it again
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested)
        {
            CompleteDisconnectTcs();
        }
        catch
        {
            CompleteDisconnectTcs();
        }
    }

    private void CompleteDisconnectTcs()
    {
        lock (_disconnectLock)
        {
            _disconnectTcs?.TrySetResult(true);
            _disconnectTcs = null;
        }
    }

    private void MarkSocketAsUserInitiated(WebSocketClient socket)
    {
        lock (_userInitiatedSocketsLock)
        {
            _userInitiatedSockets.Add(socket);
        }
    }

    private bool IsSocketUserInitiated(WebSocketClient? socket)
    {
        if (socket == null)
        {
            return false;
        }

        lock (_userInitiatedSocketsLock)
        {
            return _userInitiatedSockets.Contains(socket);
        }
    }

    private void RemoveFromUserInitiatedSockets(WebSocketClient? socket)
    {
        if (socket == null)
        {
            return;
        }

        lock (_userInitiatedSocketsLock)
        {
            _userInitiatedSockets.Remove(socket);
        }
    }

    /// <summary>
    /// Closes the socket with intentional disconnect flag set.
    /// This ensures the WebSocketClient receive loop won't call error callbacks.
    /// Use this for user-initiated disconnects (Disconnect, ChangeServer, Dispose).
    /// </summary>
    private void CloseSocketIntentionally(WebSocketClient socket)
    {
        socket.CancelIntentionally();
        socket.Disconnect();
    }

    /// <summary>
    /// Closes the socket with intentional disconnect flag set and waits for completion.
    /// This ensures the receive loop has fully exited before returning.
    /// Use this when you need to guarantee socket cleanup before proceeding.
    /// </summary>
    private async Task CloseSocketIntentionallyAsync(WebSocketClient socket)
    {
        socket.CancelIntentionally();
        await socket.DisconnectAsync().ConfigureAwait(false);
    }

    private static bool IsNetworkDropException(Exception error)
    {
        // Only classify transport-layer exceptions as network drops
        // TLS/auth/certificate errors should NOT be classified as network drops
        
        if (error is ObjectDisposedException)
            return true;

        // SocketException = transport-level issue (DNS, connection refused, timeout)
        if (error is System.Net.Sockets.SocketException)
            return true;
        
        // IOException - check for transport messages or SocketException inner
        if (error is IOException ioEx)
        {
            // Has SocketException inner = definitely transport error
            if (ioEx.InnerException is System.Net.Sockets.SocketException)
                return true;
            
            // Well-known transport error messages (MAUI/WinHTTP often throws these without inner exception)
            var msg = ioEx.Message;
            if (msg.Contains("transport connection") || 
                msg.Contains("forcibly closed") ||
                msg.Contains("Operation canceled") ||
                msg.Contains("Operation timed out") ||
                msg.Contains("Connection reset"))
                return true;
                
            return false;
        }

        // System.TimeoutException - always network-related
        if (error is System.TimeoutException)
            return true;
        
        // Xrpl.Client.Exceptions.TimeoutException - ping timeout from RequestManager
        // This indicates network stall, not a server error
        if (error is Xrpl.Client.Exceptions.TimeoutException)
            return true;

        if (error is TaskCanceledException tce && tce.InnerException != null)
            return IsNetworkDropException(tce.InnerException);
        
        if (error is OperationCanceledException oce && oce.InnerException != null)
            return IsNetworkDropException(oce.InnerException);
        
        // WebSocketException - check inner chain and message patterns
        if (error is System.Net.WebSockets.WebSocketException wsEx)
        {
            // WebSocketException wrapping any transport exception in chain
            if (wsEx.InnerException != null && IsNetworkDropException(wsEx.InnerException))
                return true;
            
            // Check message for common network error patterns
            var msg = wsEx.Message;
            if (msg.Contains("Unable to connect") ||
                msg.Contains("connect to the remote server") ||
                msg.Contains("connection was closed") ||
                msg.Contains("Connection reset"))
                return true;
                
            return false;
        }
        
        // HttpRequestException - check inner chain and message patterns
        if (error is System.Net.Http.HttpRequestException httpEx)
        {
            // HttpRequestException wrapping any transport exception in chain
            if (httpEx.InnerException != null && IsNetworkDropException(httpEx.InnerException))
                return true;
            
            // Check message for DNS/connection failures
            var msg = httpEx.Message;
            if (msg.Contains("nodename nor servname") ||  // iOS/macOS DNS failure
                msg.Contains("Name or service not known") || // Linux DNS failure
                msg.Contains("Unable to connect") ||
                msg.Contains("No such host is known") ||  // Windows DNS failure
                msg.Contains("Connection refused") ||
                msg.Contains("Network is unreachable"))
                return true;
                
            return false;
        }

        // Check for platform-specific HRESULTs on any exception type
        var hresult = error.HResult;
        if (hresult == unchecked((int)0x80072EE2) || // ERROR_WINHTTP_TIMEOUT
            hresult == unchecked((int)0x80072EFD) || // ERROR_WINHTTP_CANNOT_CONNECT
            hresult == unchecked((int)0x80072EE7) || // ERROR_WINHTTP_NAME_NOT_RESOLVED  
            hresult == unchecked((int)0x80072EFE) || // ERROR_WINHTTP_CONNECTION_ERROR
            hresult == unchecked((int)0x80072F78) || // ERROR_WINHTTP_CONNECTION_RESET
            hresult == unchecked((int)0x80004005) || // E_FAIL - generic failure, often wraps network errors
            hresult == unchecked((int)0xFFFDFFFF))   // iOS/macOS DNS failure
        {
            // For E_FAIL (0x80004005), only treat as network if message matches
            if (hresult == unchecked((int)0x80004005))
            {
                var msg = error.Message;
                if (msg.Contains("Unable to connect") ||
                    msg.Contains("connect to the remote server"))
                    return true;
                // E_FAIL with other messages might be TLS/auth - check inner
                if (error.InnerException != null)
                    return IsNetworkDropException(error.InnerException);
                return false;
            }
            return true;
        }
        
        // Check message patterns on any exception type as last resort
        var exMsg = error.Message;
        if (exMsg.Contains("nodename nor servname") ||  // iOS/macOS DNS failure
            exMsg.Contains("Name or service not known") || // Linux DNS failure
            exMsg.Contains("No such host is known"))  // Windows DNS failure
            return true;

        // Check inner exception for wrapped transport errors
        if (error.InnerException != null)
            return IsNetworkDropException(error.InnerException);

        return false;
    }

    private async Task OnConnectionFailed(
        Exception error,
        WebSocketClient? errorSocket = null,
        long sessionId = 0,
        bool isPingTimeoutReconnect = false,
        bool isNetworkDropReconnect = false)
    {
        // If this is a late callback from the socket closed due to ping timeout, ignore it
        // (but not the initial call from ping handler which has isPingTimeoutReconnect=true)
        if (_pingTimeoutSocket != null && _pingTimeoutSocket == errorSocket && !isPingTimeoutReconnect)
        {
            return;
        }

        // If this is a late callback from the socket closed due to network drop, ignore it
        // (but not the initial call which has isNetworkDropReconnect=true)
        if (_networkDropSocket != null && _networkDropSocket == errorSocket && !isNetworkDropReconnect)
        {
            return;
        }

        // Detect network drop via socket's FailureReason or exception type
        var isNetworkDrop = isNetworkDropReconnect || 
                            IsNetworkDropException(error) ||
                            (errorSocket?.FailureReason == SocketFailureReason.NetworkDrop);

        var currentUserInitiatedSocket = Volatile.Read(ref _userInitiatedSocket);
        bool userInitiated;
        bool intentionalDisconnect;
        bool wasOpen;
        bool isCurrentSocket;
        var isRetiringSession = false;

        if (errorSocket != null)
        {
            // Check if this callback is from a retiring session
            lock (_sessionLock)
            {
                if (sessionId > 0)
                {
                    if (_activeSession != null)
                    {
                        if (_activeSession.SessionId == sessionId)
                        {
                            // Same session - check if marked as retiring
                            isRetiringSession = _activeSession.IsRetiring;
                        }
                        else
                        {
                            // Different session - old callback
                            isRetiringSession = true;
                        }
                    }
                }
                else
                {
                    // Fallback for callbacks without session ID (timer timeout)
                    var activeSession = _activeSession;
                    if (activeSession != null)
                    {
                        if (activeSession.Socket != errorSocket)
                        {
                            isRetiringSession = true;
                        }
                        else if (activeSession.IsRetiring)
                        {
                            isRetiringSession = true;
                        }
                    }
                }
            }

            isCurrentSocket = ws == errorSocket;
            userInitiated = currentUserInitiatedSocket == errorSocket || IsSocketUserInitiated(errorSocket);
            intentionalDisconnect = _isIntentionalDisconnect || userInitiated || isRetiringSession;
            wasOpen = errorSocket.State == WebSocketState.Open;

            // Clean up HashSet tracking for this socket (prevent memory leak)
            RemoveFromUserInitiatedSockets(errorSocket);

            // Clear _userInitiatedSocket if it matches this socket
            Interlocked.CompareExchange(ref _userInitiatedSocket, value: null, errorSocket);

            // For stale sockets (not current) or retiring sessions, do minimal cleanup
            if ((!isCurrentSocket && ws != null) || isRetiringSession)
            {
                // This is a late callback from an old socket - don't touch current connection
                if (intentionalDisconnect)
                {
                    CloseSocketIntentionally(errorSocket);
                    CompleteDisconnectTcs();
                }
                else
                {
                    errorSocket.Cancel();
                    errorSocket.Disconnect();
                }

                return;
            }

            // Only stop timer for current socket
            timer?.Stop();
            timer?.Dispose();
            timer = null;

            // Use CloseSocketIntentionally for intentional disconnect, ping timeout, or network drop
            // to suppress Critical error logging in WebSocketClient receive loop
            if (intentionalDisconnect || isPingTimeoutReconnect || isNetworkDrop)
            {
                // Track network drop socket for filtering late callbacks
                if (isNetworkDrop && !isPingTimeoutReconnect && !intentionalDisconnect)
                {
                    _networkDropSocket = errorSocket;
                }

                CloseSocketIntentionally(errorSocket);
            }
            else
            {
                errorSocket.Cancel();
                errorSocket.Disconnect();
            }

            if (isCurrentSocket)
            {
                ws = null;
            }
        }
        else
        {
            isCurrentSocket = true; // null errorSocket means operate on current ws
            intentionalDisconnect = _isIntentionalDisconnect || currentUserInitiatedSocket != null;
            wasOpen = false;

            // Only stop timer when operating on current connection
            timer?.Stop();
            timer?.Dispose();
            timer = null;

            // For null errorSocket with intentional disconnect, still need to clean up ws reference
            if (intentionalDisconnect && ws != null)
            {
                CloseSocketIntentionally(ws);
                ws = null;
            }
        }

        CompleteDisconnectTcs();

        if (intentionalDisconnect)
        {
            connectionManager.RejectAllAwaitingWithCancellation();
            SetConnectionState(XrpConnectionState.Disconnected, message: "Connection closed permanently.");
            return;
        }

        // Reject awaiting connection requests and pending requests
        // For ping timeout and network drop, use cancellation (no Critical logging in consuming apps)
        // For other failures, use exception with message
        if (isPingTimeoutReconnect || isNetworkDrop)
        {
            requestManager.RejectAllWithCancellation();
            connectionManager.RejectAllAwaitingWithCancellation();
        }
        else
        {
            connectionManager.RejectAllAwaiting(new NotConnectedException(error.Message));
        }

        // For ping timeout or network drop, use Warning severity and RestoringConnection state
        // For other failures, use Error severity and Disconnected state
        if (isPingTimeoutReconnect)
        {
            SetConnectionState(
                XrpConnectionState.RestoringConnection,
                message: "Ping failed. Reconnecting...",
                ConnectionCloseSeverity.Warning,
                reconnect: BuildReconnectInfo());
        }
        else if (isNetworkDrop)
        {
            SetConnectionState(
                XrpConnectionState.RestoringConnection,
                message: "Network connection lost. Reconnecting...",
                ConnectionCloseSeverity.Warning,
                reconnect: BuildReconnectInfo());
        }
        else
        {
            // Check if we're in a reconnect flow using the authoritative _reconnectMode flag
            // This is set before any reconnect starts and cleared only when connection is stable
            if (IsReconnectActive())
            {
                // During reconnect, use RestoringConnection with ReconnectInfo and Warning severity
                var reconnectErrorMessage = $"Connection attempt failed: {error.Message}";
                SetConnectionState(
                    XrpConnectionState.RestoringConnection,
                    reconnectErrorMessage,
                    ConnectionCloseSeverity.Warning,
                    reconnect: BuildReconnectInfo());
            }
            else
            {
                // True initial connection failure - no reconnect in progress
                var errorMessage = $"Initial connection failed: {error.Message}";
                SetConnectionState(XrpConnectionState.Disconnected, errorMessage, ConnectionCloseSeverity.Error);
            }
        }

        // Start reconnect for initial connection failures, ping timeout, or network drop
        // For ping timeout/network drop, wasOpen=true but we still need to reconnect
        if (!wasOpen || isPingTimeoutReconnect || isNetworkDrop)
        {
            if (OnDisconnect is not null)
            {
                // For ping timeout and network drop, use neutral message to avoid Critical logging
                // in consuming apps that log OnDisconnect messages as errors
                var disconnectMessage = isPingTimeoutReconnect
                    ? "Connection lost, reconnecting..."
                    : isNetworkDrop
                        ? "Network connection lost, reconnecting..."
                        : error.Message;
                await OnDisconnect?.Invoke(code: null, disconnectMessage)!;
            }

            // Only start reconnect loop if not already running
            // This prevents _reconnectAttempts from being reset when OnConnectionFailed
            // is called from within the reconnect loop (each failed attempt triggers this callback)
            // Check both _reconnectMode AND actual loop task status for accuracy
            var loopIsRunning = _reconnectLoop != null && !_reconnectLoop.IsCompleted;
            if (!loopIsRunning)
            {
                StartReconnectLoop();
            }
        }
    }

    /// <summary>
    /// Sends a message through the WebSocket connection.
    /// </summary>
    /// <param name="ws">The WebSocket client to send through.</param>
    /// <param name="message">The message to send.</param>
    /// <exception cref="DisconnectedException">Thrown when the WebSocket connection is null or closed.</exception>
    public void WebsocketSendAsync(WebSocketClient ws, string message)
    {
        if (ws == null)
            throw new DisconnectedException("WebSocket connection was closed before request could be sent");
        ws.SendMessage(message);
    }

    private async Task EnsureConnectionForRequest(RequestFailurePolicy? policyOverride = null)
    {
        if (ShouldBeConnected())
        {
            return;
        }

        CheckIfNotConnected();

        var policy = policyOverride ?? config.RequestPolicy;

        switch (policy)
        {
            case RequestFailurePolicy.ImmediateFail:
                throw new NotConnectedException();

            case RequestFailurePolicy.WaitForConnection:
                await WaitForConnectionAsync();
                if (!ShouldBeConnected())
                {
                    throw new NotConnectedException("Failed to establish connection within timeout period");
                }

                break;

            default:
                throw new NotConnectedException();
        }
    }

    private void CheckIfNotConnected()
    {
        if (_permanentlyDisconnected)
        {
            throw new NotConnectedException("Client has been disconnected. Call Connect() to reconnect.");
        }

        // Connecting or RestoringConnection states indicate an active attempt even if ws is null
        var isActiveState = _currentConnectionState == XrpConnectionState.Connecting ||
                            _currentConnectionState == XrpConnectionState.RestoringConnection;
        var noConnectionAttemptActive = ws == null && _reconnectCts == null && !isActiveState;
        if (noConnectionAttemptActive)
        {
            throw new NotConnectedException("No connection attempt in progress. Call Connect() first.");
        }
    }

    public async Task<Dictionary<string, dynamic>> Request(
        Dictionary<string, dynamic> request,
        TimeSpan? timeout = null,
        RequestFailurePolicy? policyOverride = null)
    {
        await EnsureConnectionForRequest(policyOverride);

        var _request = requestManager.CreateRequest(request, timeout: timeout ?? config.RequestTimeout);
        try
        {
            WebsocketSendAsync(ws, _request.Message);
        }
        catch (EncodingFormatException error)
        {
            requestManager.Reject(_request.Id, error);
        }

        return await _request.Promise;
    }

    public async Task<dynamic> GRequest<T, R>(
        R request,
        TimeSpan? timeout = null,
        RequestFailurePolicy? policyOverride = null)
    {
        await EnsureConnectionForRequest(policyOverride);

        var _request = requestManager.CreateGRequest<T, R>(request, timeout: timeout ?? config.RequestTimeout);
        try
        {
            WebsocketSendAsync(ws, _request.Message);
        }
        catch (EncodingFormatException error)
        {
            requestManager.Reject(_request.Id, error);
        }

        return await _request.Promise;
    }

    public string GetUrl() => url;

    public WebSocketState State() => ws?.State ?? WebSocketState.Closed;

    private bool ShouldBeConnected() => ws is { State: WebSocketState.Open, };

    private async Task OnceOpen(WebSocketClient connectedSocket, long sessionId)
    {
        // Check if this callback is from the active session (not retiring)
        bool isActiveSession;
        lock (_sessionLock)
        {
            isActiveSession = _activeSession != null &&
                              _activeSession.SessionId == sessionId &&
                              !_activeSession.IsRetiring;
        }

        if (!isActiveSession) // Callback from a retired session - ignore silently
        {
            return;
        }

        // Verify the connected socket matches current ws, or update ws if it was cleared
        if (ws == null)
        {
            // Restore ws reference from the connected socket
            ws = connectedSocket;
        }
        else if (ws != connectedSocket)
        {
            // This is a stale callback from an old socket, ignore it silently
            // Don't touch the timer - it belongs to the new connection
            return;
        }

        // Only stop timer for current socket's callback
        timer?.Stop();
        timer?.Dispose();
        timer = null;

        // Clear all reconnect state - connection is now stable
        ClearReconnectState();

        // Reset all intentional disconnect tracking now that new connection succeeded
        // This is the safe place to clear these - old socket callbacks will have already 
        // seen _isIntentionalDisconnect = true (set by ChangeServer/Disconnect before this point)
        _isIntentionalDisconnect = false;
        _pingTimeoutSocket = null; // Clear ping timeout socket tracking
        _networkDropSocket = null; // Clear network drop socket tracking
        Interlocked.Exchange(ref _userInitiatedSocket, value: null);
        lock (_userInitiatedSocketsLock)
        {
            _userInitiatedSockets.Clear();
        }

        connectedSocket.ResetIntentionalDisconnect();

        try
        {
            connectionManager.ResolveAllAwaiting();
            if (OnConnected is not null)
            {
                await OnConnected?.Invoke();
            }

            SetConnectionState(XrpConnectionState.Connected, message: $"Connected {url}");
        }
        catch (Exception error)
        {
            connectionManager.RejectAllAwaiting(error);
            await Disconnect();
            return; // Don't start ping timer if connection failed
        }
        
        // Start ping timer AFTER connection is fully established and all callbacks completed
        // This is outside try/catch to ensure it always runs on successful connection
        StartPingTimer();
        
        // Start background message processor for stream messages
        StartMessageProcessor();
    }

    private async Task OnceClose(int? code, string? description, WebSocketClient closingSocket, long sessionId)
    {
        var (severity, userMessage) = DescribeClose(code, description);

        // Check if this callback is from a retiring session using session ID
        bool isActiveSession;
        var isRetiringSession = false;
        lock (_sessionLock)
        {
            if (_activeSession != null)
            {
                if (_activeSession.SessionId == sessionId)
                {
                    // Same session - but check if it's marked as retiring
                    isActiveSession = !_activeSession.IsRetiring;
                    isRetiringSession = _activeSession.IsRetiring;
                }
                else
                {
                    // Different session - this callback is from an old session
                    isActiveSession = false;
                    isRetiringSession = true;
                }
            }
            else
            {
                isActiveSession = false;
            }
        }

        // Check if this is the current socket or a stale callback from an old socket
        var isCurrentSocket = ws == closingSocket;
        var wsWasNull = ws == null;

        var isUserInitiated = Interlocked.CompareExchange(
            ref _userInitiatedSocket,
            value: null,
            closingSocket
        ) == closingSocket;

        var isFromUserInitiatedSet = IsSocketUserInitiated(closingSocket);
        RemoveFromUserInitiatedSockets(closingSocket);

        var intentionalDisconnect =
            _isIntentionalDisconnect || isUserInitiated || isFromUserInitiatedSet || isRetiringSession;

        // For stale sockets (not current) or retiring sessions, only do minimal cleanup
        if ((!isCurrentSocket && !wsWasNull) || isRetiringSession)
        {
            // This is a late callback from an old socket - don't touch current connection state
            // Just complete the TCS if this was an intentional disconnect
            if (intentionalDisconnect)
            {
                CompleteDisconnectTcs();
            }

            return;
        }

        // Only stop ping timer for current socket
        StopPingTimerSync();

        // Check if this is a network drop (FailureReason set by WebSocketClient)
        var isNetworkDrop = closingSocket.FailureReason == SocketFailureReason.NetworkDrop;
        
        // Track network drop socket for immediate reconnect
        if (isNetworkDrop && !intentionalDisconnect)
        {
            _networkDropSocket = closingSocket;
        }

        // For intentional disconnect or network drop, use cancellation (no Critical logging)
        if (intentionalDisconnect || isNetworkDrop)
        {
            requestManager.RejectAllWithCancellation();
        }
        else
        {
            requestManager.RejectAll(
                new DisconnectedException($"websocket was closed, code: {code}, reason: {userMessage}"));
        }

        // Clear ws reference
        if (isCurrentSocket)
        {
            ws = null;
        }

        CompleteDisconnectTcs();

        if (code == null)
        {
            if (OnDisconnect is not null)
            {
                await OnDisconnect?.Invoke(code: 1011, description: "Internal error - disconnect code was undefined")!;
            }
        }
        else
        {
            if (OnDisconnect is not null)
            {
                await OnDisconnect?.Invoke(code, userMessage)!;
            }
        }

        if (intentionalDisconnect)
        {
            _reconnectAttempts = 0;
            var noReconnectMessage = $"Connection closed permanently. {userMessage}";
            SetConnectionState(XrpConnectionState.Disconnected, noReconnectMessage, ConnectionCloseSeverity.Warning);
            return;
        }

        if (ShouldReconnect(code) || code == 1000)
        {
            // Check if reconnect loop is already running - don't reset counter or start new loop
            var loopIsRunning = _reconnectLoop != null && !_reconnectLoop.IsCompleted;
            if (!loopIsRunning)
            {
                // Set _reconnectAttempts = 1 before notification so BuildReconnectInfo returns correct value
                _reconnectAttempts = 1;
                SetConnectionState(
                    XrpConnectionState.RestoringConnection,
                    userMessage,
                    severity,
                    reconnect: BuildReconnectInfo());
                StartReconnectLoop();
            }
            // else: loop is already running and will handle reconnection, don't reset _reconnectAttempts
        }
        else
        {
            _reconnectAttempts = 0;
            var noReconnectMessage = $"Connection closed permanently. {userMessage}";
            SetConnectionState(XrpConnectionState.Disconnected, noReconnectMessage, ConnectionCloseSeverity.Warning);
        }
    }

    private void StopReconnectLoop()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        _reconnectAttempts = 0;
        // Note: Do NOT clear _reconnectMode here!
        // _reconnectMode is cleared only by:
        // - OnceOpen (connection succeeded)
        // - End of ReconnectLoopAsync (loop terminated)
        // - ClearReconnectState (user-initiated disconnect)
        // This prevents race conditions where StopReconnectLoop is called during
        // fast reconnect transitions (RetireCurrentSessionAndReconnectAsync)
        _isFastReconnectActive = false; // Legacy flag for backward compatibility
    }
    
    /// <summary>
    /// Clears all reconnect state. Only called when connection is stable or user disconnects.
    /// </summary>
    private void ClearReconnectState()
    {
        StopReconnectLoop();
        _reconnectMode = ReconnectMode.None;
    }

    private void StartReconnectLoop()
    {
        // Set reconnect mode to LoopReconnect (upgrades from FastReconnect or sets from None)
        _reconnectMode = ReconnectMode.LoopReconnect;
        
        // CRITICAL: If a loop is already running, don't start another or reset the counter
        // This prevents _reconnectAttempts from being reset mid-loop when callbacks trigger
        // reconnect logic (OnceClose, OnConnectionFailed, etc.)
        var loopIsRunning = _reconnectLoop != null && !_reconnectLoop.IsCompleted;
        if (loopIsRunning)
        {
            // Loop is already running - let it continue, don't reset _reconnectAttempts
            return;
        }
        
        // If we have a valid pre-created CTS (from RetireCurrentSessionAndReconnectAsync),
        // we should reuse it. Check for this case first.
        var existingCts = _reconnectCts;
        var hasValidPreCreatedCts = existingCts != null && !existingCts.IsCancellationRequested;
        
        // If no valid pre-created CTS, create a new one
        // Only reset _reconnectAttempts when creating a FRESH CTS (new reconnect sequence)
        if (!hasValidPreCreatedCts)
        {
            // Cancel/dispose old CTS if any
            existingCts?.Cancel();
            existingCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            _reconnectAttempts = 0;
        }
        // else: Reuse existing valid CTS (pre-created for fast reconnect)
        // Don't reset _reconnectAttempts - this is continuation of existing reconnect sequence
        // Note: _reconnectLoop was already cleared by RetireCurrentSessionAndReconnectAsync
        
        _reconnectLoop = ReconnectLoopAsync(_reconnectCts.Token);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        // Don't reset _reconnectAttempts here - it may be pre-set to 1 by fast reconnect path
        // StartReconnectLoop() sets it to 0 when creating a new CTS
        
        // Clear fast reconnect flag - reconnect loop has taken ownership
        // This must happen AFTER _reconnectCts is valid (which StartReconnectLoop ensures)
        // so any pending OnConnectionFailed callbacks still see IsReconnectActive()=true via CTS
        _isFastReconnectActive = false;
        
        // For ping timeout or network drop, first attempt should be immediate (no delay)
        var isImmediateReconnect = _pingTimeoutSocket != null || _networkDropSocket != null;

        while (!ct.IsCancellationRequested)
        {
            _reconnectAttempts++;

            // Skip delay for first attempt if this is immediate reconnect (ping timeout or network drop)
            var skipDelay = isImmediateReconnect && _reconnectAttempts == 1;
            isImmediateReconnect = false; // Only affects first attempt
            
            var delay = skipDelay ? TimeSpan.Zero : CalcBackoff(_reconnectAttempts);
            var reconnectMessage = skipDelay 
                ? "Reconnecting immediately..."
                : $"Reconnecting in {delay.TotalSeconds:F1} seconds... (attempt #{_reconnectAttempts})";
            var type = ConnectionCloseSeverity.Info;
            if (_reconnectAttempts > config.MaxReconnectAttempts)
            {
                if (config.StopAfterMaxAttempts)
                {
                    SetConnectionState(
                        XrpConnectionState.Disconnected,
                        message: $"Reconnection stopped after {config.MaxReconnectAttempts} attempts.",
                        ConnectionCloseSeverity.Error);
                    break;
                }

                reconnectMessage =
                    $"Reconnection in {delay.TotalSeconds:F1} seconds... attempt #{_reconnectAttempts} (exceeded max {config.MaxReconnectAttempts}). Will keep trying, but this may indicate a persistent issue.";
                type = ConnectionCloseSeverity.Warning;
            }

            SetConnectionState(
                XrpConnectionState.RestoringConnection,
                reconnectMessage,
                type,
                reconnect: BuildReconnectInfo(delay: delay));

            if (!skipDelay)
            {
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // =====================================================
                // SESSION ISOLATION (same as ChangeServer)
                // =====================================================
                // Mark old session as retiring before creating new connection
                // so late callbacks from old socket are properly ignored.
                ConnectionSession? oldSession;
                WebSocketClient? oldSocket;
                lock (_sessionLock)
                {
                    oldSession = _activeSession;
                    oldSession?.MarkAsRetiring();
                }
                lock (_disconnectLock)
                {
                    oldSocket = ws;
                    ws = null;
                }
                
                // Mark old socket for intentional disconnect (per-socket tracking)
                if (oldSocket != null)
                {
                    MarkSocketAsUserInitiated(oldSocket);
                    oldSocket.SetIntentionalDisconnect();
                    // Fire-and-forget graceful disposal
                    _ = RetireOldSessionAsync(oldSession, oldSocket);
                }

                await ConnectInternalAsync(ct);

                if (IsConnected())
                {
                    _reconnectAttempts = 0;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Reconnect loop was cancelled (e.g., by ChangeServer or StopReconnectLoop)
                // Exit the loop quietly without logging an error
                Debug.WriteLine($"{DateTime.Now}Reconnect loop cancelled");
                break;
            }
            catch (Exception ex)
            {
                // For network exceptions, use Warning severity to avoid Critical logging in consuming apps
                var isNetworkError = IsNetworkDropException(ex);
                var severity = isNetworkError ? ConnectionCloseSeverity.Warning : ConnectionCloseSeverity.Error;
                var errorMessage = isNetworkError 
                    ? $"Reconnection attempt #{_reconnectAttempts}: network unavailable"
                    : $"Reconnection attempt #{_reconnectAttempts} failed: {ex.Message}";
                SetConnectionState(
                    XrpConnectionState.RestoringConnection,
                    errorMessage,
                    severity,
                    reconnect: BuildReconnectInfo());
            }
        }

        // Note: _pingTimeoutSocket is cleared only in OnceOpen when new connection succeeds
        // This ensures late callbacks from ping-timeout socket are still filtered
        // even if reconnect attempts fail

        // When loop exits (cancelled, max attempts, or success) and connection is not established,
        // clear the reconnect mode. If connected, OnceOpen already cleared it.
        if (!IsConnected())
        {
            _reconnectMode = ReconnectMode.None;
        }
        
        if (config.StopAfterMaxAttempts && _reconnectAttempts >= config.MaxReconnectAttempts)
        {
            _reconnectCts?.Dispose();
            _reconnectCts = null;
        }
    }

    private volatile int _pingRunning = 0;

    private Task? _pingLoopTask = null;

    private System.Threading.Timer? _wasmPingTimer;

    private void StartWasmPingTimer(CancellationTokenSource cts)
    {
        _wasmPingTimer = new System.Threading.Timer(
            callback: state =>
            {
                var innerCts = (CancellationTokenSource)state!;
                if (innerCts.IsCancellationRequested) return;

                if (Interlocked.CompareExchange(ref _pingRunning, value: 1, comparand: 0) != 0)
                    return;

                Debug.WriteLine($"{DateTime.Now}[PING-WASM] Timer fired, executing ping check...");

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Exchange(ref _currentPingTask, tcs.Task);

                _ = ExecutePingCheckAndReleaseAsync(innerCts, tcs);
            },
            state: cts,
            dueTime: 20000,
            period: 20000);
    }

    private async Task ExecutePingCheckAndReleaseAsync(CancellationTokenSource cts, TaskCompletionSource<bool> tcs)
    {
        try
        {
            await ExecutePingCheckAsync(cts);
            Debug.WriteLine($"{DateTime.Now}[PING-WASM] Ping check completed.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{DateTime.Now}[PING-WASM] Ping check error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _pingRunning, value: 0);
            tcs.TrySetResult(true);
        }
    }

    private async Task ExecutePingCheckAsync(CancellationTokenSource cts)
    {
        try
        {
            if (cts.IsCancellationRequested)
            {
                Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Early exit: CTS cancelled");
                return;
            }

            WebSocketClient? currentSocket;
            lock (_disconnectLock)
            {
                currentSocket = ws;
            }

            if (currentSocket == null || cts.IsCancellationRequested)
            {
                Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Early exit: socket={currentSocket != null}, cts={cts.IsCancellationRequested}");
                return;
            }

            var now = DateTime.UtcNow;
            var timeSinceLastActivity = lastActivityTime.HasValue
                ? (now - lastActivityTime.Value).TotalSeconds
                : double.MaxValue;

            Debug.WriteLine($"{DateTime.Now}[PING-CHECK] timeSinceLastActivity={timeSinceLastActivity:F1}s, IsConnected={IsConnected()}, State={State()}");

            if (!IsConnected())
            {
                Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Not connected (State={State()}), triggering reconnect");
                _pingTimeoutSocket = ws;
                await RetireCurrentSessionAndReconnectAsync($"Ping detected disconnected state ({State()}).");
                return;
            }

            if (!config.UseCustomPing)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Early exit: CTS cancelled before connect check");
                return;
            }

            if (timeSinceLastActivity > 60)
            {
                _pingTimeoutSocket = ws;

                await RetireCurrentSessionAndReconnectAsync("Connection timeout (no activity for 60+ seconds).");
                return;
            }

            if (timeSinceLastActivity < 30)
            {
                try
                {
                    Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Fire-and-forget keepalive ping (active connection)");
                    currentSocket?.SendMessage("{\"command\":\"ping\",\"id\":\"00000000-0000-0000-0000-000000000000\"}");
                    if (OnPing != null)
                    {
                        await OnPing.Invoke("Ping/Pong");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Keepalive send failed: {ex.Message}");
                }
                return;
            }

            try
            {
                Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Sending actual server ping...");
                if (OnPing != null)
                {
                    await OnPing.Invoke("Ping");
                }

                if (cts.IsCancellationRequested)
                {
                    return;
                }

                await Request(
                    request: new Dictionary<string, dynamic>
                    {
                        { "command", "ping" },
                    },
                    timeout: TimeSpan.FromSeconds(45),
                    RequestFailurePolicy.ImmediateFail);

                Debug.WriteLine($"{DateTime.Now}[PING-CHECK] Server pong received");
                if (OnPing != null && !cts.IsCancellationRequested)
                {
                    await OnPing.Invoke("Pong");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NotConnectedException)
            {
            }
            catch (Exception pingEx)
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                Debug.WriteLine($"{DateTime.Now}Ping request error: {pingEx.Message}");

                _pingTimeoutSocket = ws;

                await RetireCurrentSessionAndReconnectAsync("Ping failed.");
                return;
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{DateTime.Now}Ping timer error: {ex.Message}");
        }
    }

    private void StartPingTimer()
    {
        if (!config.UseCustomPing && !config.UseCheckHealth)
        {
            return;
        }

        StopPingTimerSync();

        lastActivityTime = DateTime.UtcNow;

        var cts = new CancellationTokenSource();
        _pingCts = cts;

        if (OperatingSystem.IsBrowser())
        {
            StartWasmPingTimer(cts);
        }
        else
        {
            pingTimer = new Timer(20000);
            pingTimer.Elapsed += (sender, e) =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _pingRunning, value: 1, comparand: 0) != 0)
                {
                    return;
                }

                if (cts.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _pingRunning, value: 0);
                    return;
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Exchange(ref _currentPingTask, tcs.Task);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecutePingCheckAsync(cts).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pingRunning, value: 0);
                        tcs.TrySetResult(true);
                    }
                });
            };

            pingTimer.AutoReset = true;
            pingTimer.Start();
        }
    }

    private void StopPingTimerSync()
    {
        var cts = _pingCts;
        var timer = pingTimer;
        var loopTask = _pingLoopTask;
        var wasmTimer = _wasmPingTimer;
        _pingCts = null;
        pingTimer = null;
        _pingLoopTask = null;
        _wasmPingTimer = null;

        cts?.Cancel();

        if (timer != null)
        {
            timer.Stop();
            timer.Dispose();
        }

        wasmTimer?.Dispose();

        cts?.Dispose();
        
        StopMessageProcessor();
    }

    /// <summary>
    /// Waits for the ping task to finish. Should be called AFTER rejecting pending requests
    /// so the ping handler receives OperationCanceledException and exits quickly.
    /// </summary>
    private async Task WaitForPingToFinishAsync()
    {
        // Clear the task reference
        Interlocked.Exchange(ref _currentPingTask, value: null);
        
        // Wait for _pingRunning to become 0 (ping task's finally block will reset it)
        // Since we already rejected pending requests, the ping should exit very quickly
        var startTime = DateTime.UtcNow;
        var maxWait = TimeSpan.FromSeconds(3); // Short timeout - ping should exit quickly after request rejection
        
        while (Interlocked.CompareExchange(ref _pingRunning, value: 0, comparand: 0) != 0)
        {
            if (DateTime.UtcNow - startTime > maxWait)
            {
                // Timeout - force reset _pingRunning so we don't block the fast reconnect
                Interlocked.Exchange(ref _pingRunning, value: 0);
                break;
            }
            
            await Task.Delay(20).ConfigureAwait(false);
        }
    }
    
    private async Task StopPingTimerAndWaitAsync()
    {
        StopPingTimerSync();
        await WaitForPingToFinishAsync();
    }

    private static (ConnectionCloseSeverity severity, string message) DescribeClose(int? code, string? reason)
    {
        var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Reason: {reason}";

        return code switch
        {
            1000 => (ConnectionCloseSeverity.Info, "Connection closed normally (1000)." + suffix),
            1001 => (ConnectionCloseSeverity.Warning,
                "Server unavailable or intentionally closed the connection (1001)." + suffix),
            1002 => (ConnectionCloseSeverity.Error, "Protocol error occurred (1002)." + suffix),
            1003 => (ConnectionCloseSeverity.Error, "Invalid message type received (1003)." + suffix),
            1005 => (ConnectionCloseSeverity.Warning, "Connection was closed without a close frame (1005)." + suffix),
            1006 => (ConnectionCloseSeverity.Warning,
                "Connection interrupted abnormally (1006). Network issue, server restart, or timeout." + suffix),
            1007 => (ConnectionCloseSeverity.Error, "Invalid payload data in the WebSocket frame (1007)." + suffix),
            1008 => (ConnectionCloseSeverity.Warning,
                "Policy violation (1008). Possibly due to rate limits or access rules." + suffix),
            1009 => (ConnectionCloseSeverity.Warning, "Message too large (1009)." + suffix),
            1010 => (ConnectionCloseSeverity.Error, "Mandatory WebSocket extension is missing (1010)." + suffix),
            1011 => (ConnectionCloseSeverity.Error, "Internal server error (1011)." + suffix),
            _ => (ConnectionCloseSeverity.Warning, $"Connection closed with code {code}." + suffix),
        };
    }

    private static bool ShouldReconnect(int? code) =>
        code switch
        {
            null => true,
            1000 => false,
            1002 => false,
            1003 => false,
            1007 => false,
            1010 => false,

            1001 => true,
            1005 => true,
            1008 => true,
            1009 => true,
            1011 => true,

            _ => true,
        };

    private TimeSpan CalcBackoff(int attempts)
    {
        var exponentialDelay = config.ReconnectBaseDelay.TotalSeconds * Math.Pow(x: 2, attempts);
        var cappedDelay = Math.Min(exponentialDelay, config.ReconnectMaxDelay.TotalSeconds);

        var jitterPercent = 0.25;
        var jitter = cappedDelay * jitterPercent * (2 * _random.NextDouble() - 1);

        var finalDelay = cappedDelay + jitter;
        return TimeSpan.FromSeconds(Math.Max(val1: 0, finalDelay));
    }

    private async Task IOnMessage(string message)
    {
        lastActivityTime = DateTime.UtcNow;

        BaseResponse data;
        try
        {
            data = JsonConvert.DeserializeObject<BaseResponse>(message);
        }
        catch (Exception error)
        {
            if (OnError is not null)
            {
                await OnError?.Invoke(error: "error", errorMessage: "badMessage", error.Message, message)!;
            }

            return;
        }

        if (data.Warning != null && OnWarning is not null)
        {
            await OnWarning.Invoke(data.Warning, message);
        }

        if (data.Warnings is { Count: > 0, } && OnServerWarning is not null)
        {
            await OnServerWarning.Invoke(data.Warnings, message);
        }

        if (data.Type == null && data.Error != null)
        {
            if (data.Error == "slowDown" || data.Error == "tooBusy")
            {
                var rateLimitMessage = data.Error == "slowDown"
                    ? "Rate limit warning: Server requests to slow down. Reduce request frequency to avoid connection issues."
                    : "Rate limit warning: Server is too busy. Consider implementing exponential backoff or reducing load.";

                if (OnError is not null)
                {
                    await OnError?.Invoke(error: "rate_limit", data.Error, rateLimitMessage, data)!;
                }

                return;
            }

            if (OnError is not null)
            {
                await OnError?.Invoke(error: "error", data.Error, message: "data.ErrorMessage", data)!;
            }

            if (data.Id is not null)
            {
                requestManager.HandleResponse(data);
            }
            return;
        }

        if (data.Type != null)
        {
            Enum.TryParse(value: data.Type.ToString(), result: out ResponseStreamType type);
            switch (type)
            {
                case ResponseStreamType.ledgerClosed:
                {
                    var response = JsonConvert.DeserializeObject<LedgerStream>(message);

                    if (OnLedgerClosed is not null)
                    {
                        await OnLedgerClosed.Invoke(response)!;
                    }

                    break;
                }

                case ResponseStreamType.validationReceived:
                {
                    var response = JsonConvert.DeserializeObject<ValidationStream>(message);

                    if (OnManifestReceived is not null)
                    {
                        await OnManifestReceived.Invoke(response)!;
                    }

                    break;
                }

                case ResponseStreamType.transaction:
                {
                    var response = JsonConvert.DeserializeObject<TransactionStream>(message);

                    if (OnTransaction is not null)
                    {
                        await OnTransaction.Invoke(response)!;
                    }

                    break;
                }

                case ResponseStreamType.peerStatusChange:
                {
                    var response = JsonConvert.DeserializeObject<PeerStatusStream>(message);

                    if (OnPeerStatusChange is not null)
                    {
                        await OnPeerStatusChange.Invoke(response)!;
                    }

                    break;
                }

                case ResponseStreamType.consensusPhase:
                {
                    var response = JsonConvert.DeserializeObject<ConsensusStream>(message);

                    if (OnConsensusPhase is not null)
                    {
                        await OnConsensusPhase.Invoke(response)!;
                    }

                    break;
                }

                case ResponseStreamType.path_find:
                {
                    var response = JsonConvert.DeserializeObject<PathFindStream>(message);

                    if (OnPathFind is not null)
                    {
                        await OnPathFind.Invoke(response)!;
                    }

                    break;
                }

                case ResponseStreamType.error:
                {
                    var response = JsonConvert.DeserializeObject<ErrorResponse>(message);
                    if (OnError is not null)
                    {
                        await OnError.Invoke(response.Error, response.ErrorMessage, response.ErrorCode, response);
                    }

                    break;
                }

                default:
                    break;
            }
        }

        if (data.Type == "response")
        {
            try
            {
                requestManager.HandleResponse(data);
            }
            catch (XrplException error)
            {
                if (OnError is not null)
                {
                    await OnError.Invoke(error: "error", errorMessage: "badMessage", error.Message, error);
                }
            }
            catch (Exception error)
            {
                if (OnError is not null)
                {
                    await OnError.Invoke(error: "error", errorMessage: "badMessage", error.Message, error);
                }
            }
        }
    }

    public async Task OnMessage(string message)
    {
        await IOnMessage(message);
    }

    /// <summary>
    /// Reliably detects if message is a response by scanning for top-level "id" field.
    /// 
    /// XRPL protocol observation:
    /// - Response messages always have "id" as one of the FIRST properties (typically first)
    /// - Stream messages have "type" as first property (never have top-level "id")
    /// 
    /// Optimization: Use fast string scan first, then confirm with JsonTextReader if needed.
    /// This is critical for performance under high stream load.
    /// 
    /// IMPORTANT: This method uses ONLY string scanning, no JSON parsing.
    /// In single-threaded WebAssembly, any JSON parsing overhead causes
    /// WebSocket receive delays that lead to ping timeouts.
    /// </summary>
    private bool IsLikelyResponse(string message)
    {
        if (string.IsNullOrEmpty(message) || message.Length < 10)
            return false;
        
        // PURE STRING SCAN - no JSON parsing for maximum performance
        // Response format: {"id":"...", ...} - ALWAYS has "id" property
        // Stream format: {"type":"transaction|ledgerClosed|...", ...} - never has "id"
        //
        // Note: Response messages also have "type":"response", but they ALWAYS have "id".
        // Stream messages have "type":"transaction" etc but NEVER have "id".
        // So the reliable discriminator is presence of "id" field.
        
        // Find opening brace
        var firstBrace = message.IndexOf('{');
        if (firstBrace < 0 || firstBrace + 10 >= message.Length)
            return false;
        
        // Search ENTIRE message for "id" property
        // XRPL responses can have large "result" objects before the "id" field,
        // so we can't limit the search to just the first N characters.
        // Example response: {"result":{"info":{...large data...}},"id":"...","status":"success"}
        var pos = firstBrace + 1;
        
        // Look for "id" property - this is the ONLY reliable discriminator
        var idIndex = message.IndexOf("\"id\"", pos, StringComparison.Ordinal);
        if (idIndex >= 0)
        {
            // Verify it's followed by colon (confirming it's a property name)
            // Only need to check the next few characters after "id"
            var checkEnd = Math.Min(message.Length, idIndex + 10);
            for (var i = idIndex + 4; i < checkEnd; i++)
            {
                var c = message[i];
                if (c == ':') return true; // This is a response
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
            }
        }
        
        // No "id" found - this is a stream message
        return false;
    }

    /// <summary>
    /// Starts the background message processor for stream messages.
    /// Creates a new session-bound channel and processor task.
    /// Uses Channel&lt;T&gt; for true async support in WebAssembly single-threaded environment.
    /// </summary>
    private void StartMessageProcessor()
    {
        lock (_messageProcessorLock)
        {
            // Stop any existing processor first
            StopMessageProcessorInternal();
            
            // Create new session-bound channel and CTS
            // Using bounded channel to prevent memory issues under high load
            _streamMessageChannel = System.Threading.Channels.Channel.CreateBounded<string>(new BoundedChannelOptions(10000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
            _messageProcessorCts = new CancellationTokenSource();
            
            var channel = _streamMessageChannel;
            var cts = _messageProcessorCts;
            
            // Use truly async reader - works correctly in WebAssembly single-threaded environment
            _messageProcessorTask = Task.Run(async () =>
            {
                try
                {
                    var reader = channel.Reader;
                    while (await reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var message))
                        {
                            if (cts.Token.IsCancellationRequested)
                                return;
                            
                            try
                            {
                                await ProcessStreamMessageAsync(message).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"{DateTime.Now}Stream message processing error: {ex.Message}");
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when stopping
                }
                catch (ChannelClosedException)
                {
                    // Channel was completed - expected on session end
                }
            }, cts.Token);
        }
    }

    /// <summary>
    /// Stops the background message processor and disposes resources.
    /// </summary>
    private void StopMessageProcessor()
    {
        lock (_messageProcessorLock)
        {
            StopMessageProcessorInternal();
        }
    }

    /// <summary>
    /// Internal stop logic - must be called with _messageProcessorLock held.
    /// Completes the channel, cancels the CTS, and awaits task completion.
    /// </summary>
    private void StopMessageProcessorInternal()
    {
        var channel = _streamMessageChannel;
        var cts = _messageProcessorCts;
        var task = _messageProcessorTask;
        
        _streamMessageChannel = null;
        _messageProcessorCts = null;
        _messageProcessorTask = null;
        
        // Complete the channel first to unblock WaitToReadAsync
        if (channel != null)
        {
            try { channel.Writer.Complete(); } catch { }
        }
        
        // Then cancel the CTS
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
        }
        
        // Wait for task to complete (with timeout to prevent deadlock)
        if (task != null)
        {
            try { task.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
        
        // Dispose resources
        cts?.Dispose();
    }

    /// <summary>
    /// Processes a single stream message (transaction, ledger, etc.) in the background.
    /// This is the async version of stream handling, decoupled from the receive loop.
    /// </summary>
    private async Task ProcessStreamMessageAsync(string message)
    {
        lastActivityTime = DateTime.UtcNow;

        BaseResponse data;
        try
        {
            data = JsonConvert.DeserializeObject<BaseResponse>(message);
        }
        catch (Exception error)
        {
            if (OnError is not null)
            {
                await OnError?.Invoke(error: "error", errorMessage: "badMessage", error.Message, message)!;
            }
            return;
        }

        if (data.Warning != null && OnWarning is not null)
        {
            await OnWarning.Invoke(data.Warning, message);
        }

        if (data.Warnings is { Count: > 0, } && OnServerWarning is not null)
        {
            await OnServerWarning.Invoke(data.Warnings, message);
        }

        // Process stream messages by type
        if (data.Type != null)
        {
            Enum.TryParse(value: data.Type.ToString(), result: out ResponseStreamType type);
            switch (type)
            {
                case ResponseStreamType.ledgerClosed:
                {
                    var response = JsonConvert.DeserializeObject<LedgerStream>(message);
                    if (OnLedgerClosed is not null)
                    {
                        await OnLedgerClosed.Invoke(response)!;
                    }
                    break;
                }

                case ResponseStreamType.validationReceived:
                {
                    var response = JsonConvert.DeserializeObject<ValidationStream>(message);
                    if (OnManifestReceived is not null)
                    {
                        await OnManifestReceived.Invoke(response)!;
                    }
                    break;
                }

                case ResponseStreamType.transaction:
                {
                    var response = JsonConvert.DeserializeObject<TransactionStream>(message);
                    if (OnTransaction is not null)
                    {
                        await OnTransaction.Invoke(response)!;
                    }
                    break;
                }

                case ResponseStreamType.peerStatusChange:
                {
                    var response = JsonConvert.DeserializeObject<PeerStatusStream>(message);
                    if (OnPeerStatusChange is not null)
                    {
                        await OnPeerStatusChange.Invoke(response)!;
                    }
                    break;
                }

                case ResponseStreamType.consensusPhase:
                {
                    var response = JsonConvert.DeserializeObject<ConsensusStream>(message);
                    if (OnConsensusPhase is not null)
                    {
                        await OnConsensusPhase.Invoke(response)!;
                    }
                    break;
                }

                case ResponseStreamType.path_find:
                {
                    var response = JsonConvert.DeserializeObject<PathFindStream>(message);
                    if (OnPathFind is not null)
                    {
                        await OnPathFind.Invoke(response)!;
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Fast-path message handler that prioritizes request responses over stream data.
    /// This prevents ping timeouts by ensuring pong responses are processed immediately,
    /// while stream messages are queued for background processing.
    /// 
    /// Threading Model:
    /// - Response handling (requestManager.HandleResponse) is SYNCHRONOUS and immediate
    /// - Warning/error callbacks are dispatched via fire-and-forget Task.Run for performance
    /// - Stream messages are queued to a background processor
    /// 
    /// IMPORTANT: Event handlers (OnWarning, OnError, OnServerWarning) may be invoked
    /// concurrently from the ThreadPool. Handler implementations MUST be thread-safe
    /// or marshal to their own synchronization context (e.g., UI thread).
    /// </summary>
    private async Task IOnMessageFastPath(string message)
    {
        lastActivityTime = DateTime.UtcNow;

        // Scan message for "id" property to detect response messages
        var isResponse = IsLikelyResponse(message);
        
        if (isResponse)
        {
            // This is a response (including ping/pong) - process immediately with full parsing
            // CRITICAL: Minimize async operations here to prevent blocking subsequent messages
            BaseResponse data;
            try
            {
                data = JsonConvert.DeserializeObject<BaseResponse>(message);
            }
            catch (Exception error)
            {
                // Fire-and-forget for error callback - don't block
                _ = Task.Run(async () =>
                {
                    if (OnError is not null)
                    {
                        await OnError.Invoke(error: "error", errorMessage: "badMessage", error.Message, message);
                    }
                });
                return;
            }
            
            // FIRST: Handle response immediately to unblock any waiting requests (like ping)
            // This is the most time-critical operation
            if (data.Id is not null)
            {
                requestManager.HandleResponse(data);
            }

            // THEN: Handle warnings and errors in background (fire-and-forget)
            // These are informational and should not delay response processing
            if (data.Warning != null || data.Warnings is { Count: > 0 } || (data.Type == null && data.Error != null))
            {
                var capturedData = data;
                var capturedMessage = message;
                _ = Task.Run(async () =>
                {
                    if (capturedData.Warning != null && OnWarning is not null)
                    {
                        await OnWarning.Invoke(capturedData.Warning, capturedMessage);
                    }

                    if (capturedData.Warnings is { Count: > 0 } && OnServerWarning is not null)
                    {
                        await OnServerWarning.Invoke(capturedData.Warnings, capturedMessage);
                    }

                    // Handle error responses
                    if (capturedData.Type == null && capturedData.Error != null)
                    {
                        if (capturedData.Error == "slowDown" || capturedData.Error == "tooBusy")
                        {
                            var rateLimitMessage = capturedData.Error == "slowDown"
                                ? "Rate limit warning: Server requests to slow down. Reduce request frequency to avoid connection issues."
                                : "Rate limit warning: Server is too busy. Consider implementing exponential backoff or reducing load.";

                            if (OnError is not null)
                            {
                                await OnError.Invoke(error: "rate_limit", capturedData.Error, rateLimitMessage, capturedData);
                            }
                            return;
                        }

                        if (OnError is not null)
                        {
                            await OnError.Invoke(error: "error", capturedData.Error, message: "data.ErrorMessage", capturedData);
                        }
                    }
                });
            }
        }
        else
        {
            // This is a stream message (no "id") - process asynchronously
            // to avoid blocking the receive loop and causing ping timeouts
            
            if (OperatingSystem.IsBrowser())
            {
                // WebAssembly is single-threaded - Channel/Task.Run don't work as expected
                // Use fire-and-forget with ConfigureAwait(false) to allow continuation scheduling
                // This queues the work to run after the current synchronous block completes
                _ = ProcessStreamMessageFireAndForgetAsync(message);
            }
            else
            {
                // Desktop/MAUI - use Channel for true background processing
                var channel = _streamMessageChannel;
                if (channel != null)
                {
                    if (!channel.Writer.TryWrite(message))
                    {
                        // Channel is full or completed - oldest messages are dropped automatically
                        // with BoundedChannelFullMode.DropOldest
                        Debug.WriteLine($"{DateTime.Now}Warning: Stream message channel full, message dropped");
                    }
                }
                else
                {
                    // Channel not available - fall back to fire-and-forget
                    _ = ProcessStreamMessageFireAndForgetAsync(message);
                }
            }
        }
    }
    
    /// <summary>
    /// Fire-and-forget stream message processing for single-threaded environments like WebAssembly.
    /// Uses ConfigureAwait(false) to prevent deadlocks and allow proper continuation scheduling.
    /// </summary>
    private async Task ProcessStreamMessageFireAndForgetAsync(string message)
    {
        try
        {
            await ProcessStreamMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{DateTime.Now}Stream message processing error: {ex.Message}");
        }
    }
}