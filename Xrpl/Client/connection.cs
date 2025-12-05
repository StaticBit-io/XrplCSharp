using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xrpl.Client.Exceptions;
using static Xrpl.Client.RequestManager;
using Xrpl.AddressCodec;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Methods;
using Timer = System.Timers.Timer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/client/connection.ts

namespace Xrpl.Client
{
    public enum ConnectionCloseSeverity { Info, Warning, Error }

    public enum RequestFailurePolicy
    {
        ImmediateFail,
        WaitForConnection
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
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
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
            /// Default: 20 seconds.
            /// </summary>
            public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

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
            public int MaxReconnectAttempts { get; set; } = 10;

            /// <summary>
            /// Gets or sets a value indicating whether the operation should stop after reaching the maximum number of
            /// attempts.
            /// </summary>
            /// <remarks>Set this property to <see langword="true"/> to prevent further retries once
            /// the maximum attempt count is reached. If set to <see langword="false"/>, the operation may continue
            /// beyond the maximum attempts, depending on the retry policy.</remarks>
            public bool StopAfterMaxAttempts { get; set; } = false;

            /// <summary>
            /// Gets or sets a value indicating whether to use a custom ping<br/>
            /// implementation instead of the default behavior.
            /// </summary>
            public bool UseCustomPing { get; set; } = true;

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

        static WebSocketClient CreateWebSocket(string url, ConnectionOptions config)
        {
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
            return WebSocketClient.Create(url); // todo add options
        }

        public string url { get; private set; }
        public WebSocketClient ws;

        private int? reconnectTimeoutID = null;
        private int? heartbeatIntervalID = null;
        private int _reconnectAttempts = 0;
        private static readonly Random _random = new Random();
        private System.Threading.CancellationTokenSource _reconnectCts;
        private Task _reconnectLoop;
        private System.Threading.SemaphoreSlim _connectLock = new System.Threading.SemaphoreSlim(1, 1);

        private DateTime? lastActivityTime = null;
        private Timer? pingTimer = null;
        private WebSocketClient? _userInitiatedSocket = null;
        private volatile bool _permanentlyDisconnected = false;

        public ConnectionOptions config { get; private set; }
        public RequestManager requestManager = new RequestManager();
        public ConnectionManager connectionManager = new ConnectionManager();

        public Connection(string server, ConnectionOptions? options = null)
        {
            url = server;
            config = options ?? new ConnectionOptions();

            ValidateConfig();
        }

        public async Task ChangeServer(string server, ConnectionOptions? options = null, CancellationToken cancellationToken = default)
        {
            await Disconnect();
            url = server;
            if (options != null)
            {
                config = options;
            }

            ValidateConfig();

            await Task.Delay(3000, cancellationToken);
            await Connect(cancellationToken);
        }
        public bool IsConnected()
        {
            return this.State() == WebSocketState.Open;
        }

        public async Task WaitForConnectionAsync(TimeSpan? timeout = null, System.Threading.CancellationToken cancellationToken = default)
        {
            if (IsConnected())
                return;

            CheckIfNotConnected();

            var waitTimeout = timeout ?? config.ConnectionAcquisitionTimeout;

            if (waitTimeout != System.Threading.Timeout.InfiniteTimeSpan && waitTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), 
                    $"Timeout must be positive or Timeout.InfiniteTimeSpan, but was {waitTimeout.TotalSeconds:F1}s");
            }

            var startTime = DateTime.UtcNow;
            var checkInterval = TimeSpan.FromMilliseconds(100);
            var hasTimeout = waitTimeout != System.Threading.Timeout.InfiniteTimeSpan;

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
                    throw new System.TimeoutException($"Connection was not established within {waitTimeout.TotalSeconds:F1} seconds");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Connection wait was cancelled", cancellationToken);
                }

                try
                {
                    await Task.Delay(checkInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new OperationCanceledException("Connection wait was cancelled", cancellationToken);
                }
            }
        }

        public async Task<bool> HasConnectionAsync(TimeSpan? timeout = null)
        {
            try
            {
                await WaitForConnectionAsync(timeout, System.Threading.CancellationToken.None);
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
            StopReconnectLoop();
            await ConnectInternalAsync();
            await WaitForConnectionAsync(config.ConnectionAcquisitionTimeout, cancellationToken);
        }

        private async Task ConnectInternalAsync()
        {
            _permanentlyDisconnected = false;
            await _connectLock.WaitAsync();
            try
            {
                if (this.IsConnected())
                {
                    return;
                }
                if (this.State() == WebSocketState.Connecting)
                {
                    await this.connectionManager.AwaitConnection();
                    return;
                }
                if (this.url == null)
                {
                    throw new ConnectionException("Cannot connect because no server was specified");
                }
                if (this.ws != null)
                {
                    throw new XrplException("Websocket connection never cleaned up.");
                }

                timer = new Timer(this.config.ConnectionAttemptTimeout.TotalMilliseconds);
                timer.Elapsed += async (sender, e) => await OnConnectionFailed(new ConnectionException($"Error: connect() timed out after {this.config.ConnectionAttemptTimeout.TotalSeconds:F1} seconds. If your internet connection is working, the rippled server may be blocked or inaccessible. You can also try setting the 'ConnectionAttemptTimeout' option in the Client constructor."), this.ws);
                timer.Start();

                this.ws = CreateWebSocket(this.url, this.config);
                if (this.ws == null)
                {
                    throw new XrplException("Connect: created null websocket");
                }

                ws.OnConnect(async (ws) => { await OnceOpen(); });

                ws.OnConnectionError(async (e, errorSocket) =>
                {
                    timer.Stop();
                    await OnConnectionFailed(e, errorSocket);
                });

                ws.OnMessageReceived(async (m, ws) => { await IOnMessage(m); });
                ws.OnDisconnect(async (closeStatus, closeDescription, closingSocket) =>
                {
                    timer.Stop();
                    int? code = (int?)closeStatus;
                    await OnceClose(code, closeDescription, closingSocket);
                });

                await this.ws.Connect();

                this.connectionManager.AwaitConnection();
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async Task<int> Disconnect()
        {
            _permanentlyDisconnected = true;
            StopReconnectLoop();
            StopPingTimer();

            if (ws == null)
            {
                return 0;
            }

            Interlocked.Exchange(ref _userInitiatedSocket, ws);
            ws.Disconnect();

            return 0;
        }

        private async Task OnConnectionFailed(Exception error, WebSocketClient? errorSocket = null)
        {
            timer?.Stop();
            timer?.Dispose();
            timer = null;

            var userInitiated = errorSocket != null
                ? Interlocked.CompareExchange(ref _userInitiatedSocket, null, errorSocket) == errorSocket
                : Interlocked.Exchange(ref _userInitiatedSocket, null) != null;
            var wasOpen = this.ws?.State == WebSocketState.Open;

            if (this.ws != null)
            {
                //this.ws.RemoveAllListeners();
                //this.ws.on('error', () => {
                /*
                * Correctly listen for -- but ignore -- any future errors: If you
                * don't have a listener on "error" node would log a warning on error.
                */
                //});
                this.ws.Disconnect();
                this.ws = null;
            }
            this.connectionManager.RejectAllAwaiting(new NotConnectedException(error.Message));

            if (userInitiated)
            {
                OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                {
                    Message = "Connection closed permanently. " + error.Message,
                    Severity = ConnectionCloseSeverity.Info,
                    Reconnect = null
                });
                if (OnDisconnect is not null)
                    await OnDisconnect?.Invoke(null, error.Message)!;

                return;
            }

            var errorMessage = $"Initial connection failed: {error.Message}";
            OnConnectionStatus?.Invoke(new ConnectionStatusInfo
            {
                Message = errorMessage,
                Severity = ConnectionCloseSeverity.Error,
                Reconnect = null
            });

            if (!wasOpen)
            {
                if (OnDisconnect is not null)
                    await OnDisconnect?.Invoke(null, error.Message)!;

                StartReconnectLoop();
            }
        }

        public void WebsocketSendAsync(WebSocketClient ws, string message)
        {
            ws.SendMessage(message);
        }

        private async Task EnsureConnectionForRequest(RequestFailurePolicy? policyOverride = null)
        {
            if (this.ShouldBeConnected())
                return;

            CheckIfNotConnected();

            var policy = policyOverride ?? config.RequestPolicy;

            switch (policy)
            {
                case RequestFailurePolicy.ImmediateFail:
                    throw new NotConnectedException();

                case RequestFailurePolicy.WaitForConnection:
                    await WaitForConnectionAsync();
                    if (!this.ShouldBeConnected())
                        throw new NotConnectedException("Failed to establish connection within timeout period");
                    break;

                default:
                    throw new NotConnectedException();
            }
        }

        private void CheckIfNotConnected()
        {
            if (_permanentlyDisconnected)
                throw new NotConnectedException("Client has been disconnected. Call Connect() to reconnect.");

            var noConnectionAttemptActive = this.ws == null && _reconnectCts == null;
            if (noConnectionAttemptActive)
                throw new NotConnectedException("No connection attempt in progress. Call Connect() first.");
        }

        public async Task<Dictionary<string, dynamic>> Request(Dictionary<string, dynamic> request, TimeSpan? timeout = null, RequestFailurePolicy? policyOverride = null)
        {
            await EnsureConnectionForRequest(policyOverride);

            XrplRequest _request = this.requestManager.CreateRequest(request, timeout ?? this.config.RequestTimeout);
            try
            {
                WebsocketSendAsync(this.ws, _request.Message);
            }
            catch (EncodingFormatException error)
            {
                this.requestManager.Reject(_request.Id, error);
            }
            return await _request.Promise;
        }

        public async Task<dynamic> GRequest<T, R>(R request, TimeSpan? timeout = null, RequestFailurePolicy? policyOverride = null)
        {
            await EnsureConnectionForRequest(policyOverride);

            XrplGRequest _request = this.requestManager.CreateGRequest<T, R>(request, timeout ?? this.config.RequestTimeout);
            try
            {
                WebsocketSendAsync(this.ws, _request.Message);
            }
            catch (EncodingFormatException error)
            {
                this.requestManager.Reject(_request.Id, error);
            }
            return await _request.Promise;
        }

        public string GetUrl()
        {
            return this.url;
        }

        public WebSocketState State() => ws?.State ?? WebSocketState.Closed;

        private bool ShouldBeConnected()
        {
            return this.ws is { State: WebSocketState.Open };
        }

        private async Task OnceOpen()
        {
            if (this.ws == null)
            {
                throw new XrplException("onceOpen: ws is null");
            }

            timer.Stop();
            StopReconnectLoop();
            StartPingTimer();

            try
            {
                this.connectionManager.ResolveAllAwaiting();
                if (OnConnected is not null)
                    await this.OnConnected?.Invoke();
                OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                {
                    Message = $"Connected {url}",
                    Severity = ConnectionCloseSeverity.Info,
                    Reconnect = null
                });
            }
            catch (Exception error)
            {
                this.connectionManager.RejectAllAwaiting(error);
                await this.Disconnect();
            }
        }

        private async Task OnceClose(int? code, string? description, WebSocketClient closingSocket)
        {
            StopPingTimer();

            var (severity, userMessage) = DescribeClose(code, description);

            this.requestManager.RejectAll(new DisconnectedException($"websocket was closed, code: {code}, reason: {userMessage}"));
            this.ws = null;

            if (code == null)
            {
                if (OnDisconnect is not null)
                    await OnDisconnect?.Invoke(1011, "Internal error - disconnect code was undefined")!;
            }
            else
            {
                if (OnDisconnect is not null)
                    await OnDisconnect?.Invoke(code, userMessage)!;
            }

            var isUserInitiated = Interlocked.CompareExchange(
                ref _userInitiatedSocket,
                null,
                closingSocket
            ) == closingSocket;

            if (isUserInitiated)
            {
                _reconnectAttempts = 0;
                var noReconnectMessage = $"Connection closed permanently. {userMessage}";
                OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                {
                    Message = noReconnectMessage,
                    Severity = ConnectionCloseSeverity.Warning,
                    Reconnect = null
                });
                return;
            }

            if (ShouldReconnect(code) || code == 1000)
            {
                OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                {
                    Message = userMessage,
                    Severity = severity,
                    Reconnect = null
                });
                StartReconnectLoop();
            }
            else
            {
                _reconnectAttempts = 0;
                var noReconnectMessage = $"Connection closed permanently. {userMessage}";
                OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                {
                    Message = noReconnectMessage,
                    Severity = ConnectionCloseSeverity.Warning,
                    Reconnect = null
                });
            }
        }

        private void StopReconnectLoop()
        {
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            _reconnectAttempts = 0;
        }

        private void StartReconnectLoop()
        {
            if (_reconnectLoop != null && !_reconnectLoop.IsCompleted)
            {
                return;
            }

            StopReconnectLoop();
            _reconnectCts = new System.Threading.CancellationTokenSource();
            _reconnectLoop = ReconnectLoopAsync(_reconnectCts.Token);
        }

        private async Task ReconnectLoopAsync(System.Threading.CancellationToken ct)
        {
            _reconnectAttempts = 0;

            while (!ct.IsCancellationRequested)
            {
                _reconnectAttempts++;

                var delay = CalcBackoff(_reconnectAttempts);
                var reconnectMessage = $"Reconnecting in {delay.TotalSeconds:F1} seconds... (attempt #{_reconnectAttempts})";
                var type = ConnectionCloseSeverity.Info;
                if (_reconnectAttempts > config.MaxReconnectAttempts)
                {
                    if (config.StopAfterMaxAttempts)
                    {
                        OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                        {
                            Message = $"Reconnection stopped after {config.MaxReconnectAttempts} attempts.",
                            Severity = ConnectionCloseSeverity.Error,
                            Reconnect = null
                        });
                        break;
                    }

                    reconnectMessage = $"Reconnection in {delay.TotalSeconds:F1} seconds... attempt #{_reconnectAttempts} (exceeded max {config.MaxReconnectAttempts}). Will keep trying, but this may indicate a persistent issue.";
                    type = ConnectionCloseSeverity.Warning;
                }
                OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                {
                    Message = reconnectMessage,
                    Severity = type,
                    Reconnect = new ReconnectInfo
                    {
                        CurrentAttempt = _reconnectAttempts,
                        MaxAttempts = config.MaxReconnectAttempts,
                        RemainingDelay = delay
                    }
                });

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await ConnectInternalAsync();

                    if (IsConnected())
                    {
                        _reconnectAttempts = 0;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Reconnection attempt #{_reconnectAttempts} failed: {ex.Message}";
                    OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                    {
                        Message = errorMessage,
                        Severity = ConnectionCloseSeverity.Error,
                        Reconnect = null
                    });
                }
            }

            if (config.StopAfterMaxAttempts && _reconnectAttempts >= config.MaxReconnectAttempts)
            {
                _reconnectCts?.Dispose();
                _reconnectCts = null;
            }
        }

        private bool _pingTimerRunning = false;

        private void StartPingTimer()
        {
            if (!config.UseCustomPing)
            {
                return;
            }

            StopPingTimer();

            lastActivityTime = DateTime.UtcNow;

            pingTimer = new Timer(20000);
            pingTimer.Elapsed += async (sender, e) =>
            {
                if (_pingTimerRunning)
                    return;

                try
                {
                    _pingTimerRunning = true;

                    var now = DateTime.UtcNow;
                    var timeSinceLastActivity = lastActivityTime.HasValue
                        ? (now - lastActivityTime.Value).TotalSeconds
                        : double.MaxValue;

                    if (timeSinceLastActivity > 60)
                    {
                        StopPingTimer();
                        OnConnectionStatus?.Invoke(new ConnectionStatusInfo
                        {
                            Message = "Connection timeout detected (no activity for 60+ seconds). Reconnecting...",
                            Severity = ConnectionCloseSeverity.Error,
                            Reconnect = null
                        });

                        this.ws?.Disconnect();
                        this.ws = null;
                        StartReconnectLoop();
                        return;
                    }

                    if (!IsConnected())
                    {
                        return;
                    }

                    try
                    {
                        if (OnPing != null)
                        {
                            await OnPing.Invoke("Ping");
                        }
                        await Request(new Dictionary<string, dynamic> { { "command", "ping" } }, null, RequestFailurePolicy.ImmediateFail);
                        if (OnPing != null)
                        {
                            await OnPing.Invoke("Pong");
                        }
                    }
                    catch (NotConnectedException)
                    {
                    }
                    catch (Exception pingEx)
                    {
                        Console.WriteLine($"Ping request error: {pingEx.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ping timer error: {ex.Message}");
                }
                finally
                {
                    _pingTimerRunning = false;
                }
            };
            pingTimer.AutoReset = true;
            pingTimer.Start();
        }

        private void StopPingTimer()
        {
            if (pingTimer != null)
            {
                pingTimer.Stop();
                pingTimer.Dispose();
                pingTimer = null;
            }
        }

        private static (ConnectionCloseSeverity severity, string message) DescribeClose(int? code, string? reason)
        {
            var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Reason: {reason}";

            return code switch
            {
                1000 => (ConnectionCloseSeverity.Info, "Connection closed normally (1000)." + suffix),
                1001 => (ConnectionCloseSeverity.Warning, "Server unavailable or intentionally closed the connection (1001)." + suffix),
                1002 => (ConnectionCloseSeverity.Error, "Protocol error occurred (1002)." + suffix),
                1003 => (ConnectionCloseSeverity.Error, "Invalid message type received (1003)." + suffix),
                1005 => (ConnectionCloseSeverity.Warning, "Connection was closed without a close frame (1005)." + suffix),
                1006 => (ConnectionCloseSeverity.Warning, "Connection interrupted abnormally (1006). Network issue, server restart, or timeout." + suffix),
                1007 => (ConnectionCloseSeverity.Error, "Invalid payload data in the WebSocket frame (1007)." + suffix),
                1008 => (ConnectionCloseSeverity.Warning, "Policy violation (1008). Possibly due to rate limits or access rules." + suffix),
                1009 => (ConnectionCloseSeverity.Warning, "Message too large (1009)." + suffix),
                1010 => (ConnectionCloseSeverity.Error, "Mandatory WebSocket extension is missing (1010)." + suffix),
                1011 => (ConnectionCloseSeverity.Error, "Internal server error (1011)." + suffix),
                _ => (ConnectionCloseSeverity.Warning, $"Connection closed with code {code}." + suffix)
            };
        }

        private static bool ShouldReconnect(int? code) => code switch
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

            _ => true
        };

        private TimeSpan CalcBackoff(int attempts)
        {
            var exponentialDelay = config.ReconnectBaseDelay.TotalSeconds * Math.Pow(2, attempts);
            var cappedDelay = Math.Min(exponentialDelay, config.ReconnectMaxDelay.TotalSeconds);

            var jitterPercent = 0.25;
            var jitter = cappedDelay * jitterPercent * (2 * _random.NextDouble() - 1);

            var finalDelay = cappedDelay + jitter;
            return TimeSpan.FromSeconds(Math.Max(0, finalDelay));
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
                    await OnError?.Invoke("error", "badMessage", error.Message, message)!;
                return;
            }

            if (data.Warning != null && OnWarning is not null)
            {
                await OnWarning.Invoke(data.Warning, message);
            }
            if (data.Warnings is { Count: > 0 } && OnServerWarning is not null)
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
                        await OnError?.Invoke("rate_limit", data.Error, rateLimitMessage, data)!;

                    return;
                }

                if (OnError is not null)
                    await OnError?.Invoke("error", data.Error, "data.ErrorMessage", data)!;
                return;
            }
            if (data.Type != null)
            {
                Enum.TryParse(data.Type.ToString(), out ResponseStreamType type);
                switch (type)
                {
                    case ResponseStreamType.ledgerClosed:
                        {
                            var response = JsonConvert.DeserializeObject<LedgerStream>(message);

                            if (OnLedgerClosed is not null)
                                await OnLedgerClosed.Invoke(response)!;
                            break;
                        }
                    case ResponseStreamType.validationReceived:
                        {
                            var response = JsonConvert.DeserializeObject<ValidationStream>(message);

                            if (OnManifestReceived is not null)
                                await OnManifestReceived.Invoke(response)!;
                            break;
                        }
                    case ResponseStreamType.transaction:
                        {
                            var response = JsonConvert.DeserializeObject<TransactionStream>(message);

                            if (OnTransaction is not null)
                                await OnTransaction.Invoke(response)!;
                            break;
                        }
                    case ResponseStreamType.peerStatusChange:
                        {
                            var response = JsonConvert.DeserializeObject<PeerStatusStream>(message);

                            if (OnPeerStatusChange is not null)
                                await OnPeerStatusChange.Invoke(response)!;
                            break;
                        }
                    case ResponseStreamType.consensusPhase:
                        {
                            var response = JsonConvert.DeserializeObject<ConsensusStream>(message);

                            if (OnConsensusPhase is not null)
                                await OnConsensusPhase.Invoke(response)!;
                            break;
                        }
                    case ResponseStreamType.path_find:
                        {
                            var response = JsonConvert.DeserializeObject<PathFindStream>(message);

                            if (OnPathFind is not null)
                                await OnPathFind.Invoke(response)!;
                            break;
                        }
                    case ResponseStreamType.error:
                        {
                            var response = JsonConvert.DeserializeObject<ErrorResponse>(message);
                            if (OnError is not null)
                                await OnError.Invoke(response.Error, response.ErrorMessage, response.ErrorCode, response);
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
                    this.requestManager.HandleResponse(data);
                }
                catch (XrplException error)
                {
                    if (OnError is not null)
                        await OnError.Invoke("error", "badMessage", error.Message, error);
                }
                catch (Exception error)
                {
                    if (OnError is not null)
                        await OnError.Invoke("error", "badMessage", error.Message, error);
                }
            }
        }

        public async Task OnMessage(string message)
        {
            await IOnMessage(message);
        }
    }
}