using System;
using System.Collections.Generic;
using System.Net.WebSockets;
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
    public class Connection
    {

        public event OnError OnError;
        public event OnWarning OnWarning;
        public event OnWarning2 OnWarning2;
        public event OnConnected OnConnected;
        public event OnDisconnect OnDisconnect;
        public event OnLedgerClosed OnLedgerClosed;
        public event OnTransaction OnTransaction;
        public event OnManifestReceived OnManifestReceived;
        public event OnPeerStatusChange OnPeerStatusChange;
        public event OnConsensusPhase OnConsensusPhase;
        public event OnPathFind OnPathFind;
        public event Action<string> OnConnectionStatus;

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
            public int timeout { get; set; }
            public int connectionTimeout { get; set; }
            public Dictionary<string, dynamic> headers { get; set; }
            
            public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
            public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
            public int MaxReconnectAttempts { get; set; } = 10;
        }

        private enum CloseSeverity { Info, Warn, Error }

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

        int TIMEOUT = 20;
        int CONNECTION_TIMEOUT = 5;
        int INTENTIONAL_DISCONNECT_CODE = 4000;

        public string url { get; private set; }
        public WebSocketClient ws;

        private int? reconnectTimeoutID = null;
        private int? heartbeatIntervalID = null;
        private int _reconnectAttempts = 0;
        private static readonly Random _random = new Random();
        private System.Threading.CancellationTokenSource _reconnectCts;
        private Task _reconnectLoop;
        private System.Threading.SemaphoreSlim _connectLock = new System.Threading.SemaphoreSlim(1, 1);

        public ConnectionOptions config { get; private set; }
        public RequestManager requestManager = new RequestManager();
        public ConnectionManager connectionManager = new ConnectionManager();

        public Connection(string server, ConnectionOptions? options = null)
        {
            url = server;
            config = options ?? new ConnectionOptions();
            config.timeout = TIMEOUT * 1000;
            config.connectionTimeout = CONNECTION_TIMEOUT * 1000;

        }

        public async Task ChangeServer(string server, ConnectionOptions? options = null)
        {
            await Disconnect();
            url = server;
            config = options ?? new ConnectionOptions();
            config.timeout = TIMEOUT * 1000;
            config.connectionTimeout = CONNECTION_TIMEOUT * 1000;
            await Task.Delay(3000);
            await Connect();
        }
        public bool IsConnected()
        {
            return this.State() == WebSocketState.Open;
        }

        public Timer timer;

        public async Task Connect()
        {
            StopReconnectLoop();
            await ConnectInternalAsync();
        }

        private async Task ConnectInternalAsync()
        {
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
                
                timer = new Timer(this.config.connectionTimeout);
                timer.Elapsed += async (sender, e) => await OnConnectionFailed(new ConnectionException($"Error: connect() timed out after {this.config.connectionTimeout} ms.If your internet connection is working, the rippled server may be blocked or inaccessible.You can also try setting the 'connectionTimeout' option in the Client constructor."));
                timer.Start();

                this.ws = CreateWebSocket(this.url, this.config);
                if (this.ws == null)
                {
                    throw new XrplException("Connect: created null websocket");
                }

                ws.OnConnect(async (ws) => { await OnceOpen(); });

                ws.OnConnectionError(async (e, ws) =>
                {
                    timer.Stop();
                    await OnConnectionFailed(e);
                });

                ws.OnMessageReceived(async (m, ws) => { await IOnMessage(m); });
                ws.OnDisconnect(async (closeStatus, closeDescription, ws) =>
                {
                    timer.Stop();
                    int? code = (int?)closeStatus;
                    await OnceClose(code, closeDescription);
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
            StopReconnectLoop();
            
            if (ws == null)
            {
                return 0;
            }
            var result = 0;
            if (ws != null)
            {
                ws.Disconnect();
                //ws.OnDisconnect += (code) => { result = code; };
            }

            return result;
        }

        private async Task OnConnectionFailed(Exception error)
        {
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
        }

        public void WebsocketSendAsync(WebSocketClient ws, string message)
        {
            ws.SendMessage(message);
        }

        public async Task<Dictionary<string, dynamic>> Request(Dictionary<string, dynamic> request, int? timeout = null)
        {
            if (!this.ShouldBeConnected())
            {
                throw new NotConnectedException();
            }
            XrplRequest _request = this.requestManager.CreateRequest(request, timeout ?? this.config.timeout);
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

        public async Task<dynamic> GRequest<T, R>(R request, int? timeout = null)
        {
            if (!this.ShouldBeConnected())
            {
                throw new NotConnectedException();
            }
            XrplGRequest _request = this.requestManager.CreateGRequest<T, R>(request, timeout ?? this.config.timeout);
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

            try
            {
                this.connectionManager.ResolveAllAwaiting();
                if (OnConnected is not null)
                    await this.OnConnected?.Invoke();
            }
            catch (Exception error)
            {
                this.connectionManager.RejectAllAwaiting(error);
                await this.Disconnect();
            }
        }

        private async Task OnceClose(int? code, string? description = null)
        {
            var reasonText = string.IsNullOrWhiteSpace(description) 
                ? "Unknown reason" 
                : description;
            
            var (severity, userMessage) = DescribeClose(code, reasonText);
            
            this.requestManager.RejectAll(new DisconnectedException($"websocket was closed, code: {code}, reason: {reasonText}"));
            this.ws = null;
            
            if (code == null)
            {
                if (OnDisconnect is not null)
                    await OnDisconnect?.Invoke(1011, "Internal error - disconnect code was undefined")!;
            }
            else
            {
                if (OnDisconnect is not null)
                    await OnDisconnect?.Invoke(code, description)!;
            }
            
            OnConnectionStatus?.Invoke(userMessage);

            if (code == INTENTIONAL_DISCONNECT_CODE)
            {
                _reconnectAttempts = 0;
                return;
            }

            if (ShouldReconnect(code))
            {
                StartReconnectLoop();
            }
            else
            {
                _reconnectAttempts = 0;
                var noReconnectMessage = $"Connection closed permanently. {userMessage}";
                OnConnectionStatus?.Invoke(noReconnectMessage);
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

                if (_reconnectAttempts > config.MaxReconnectAttempts)
                {
                    var warning = $"Reconnection attempt #{_reconnectAttempts} (exceeded max {config.MaxReconnectAttempts}). Will keep trying, but this may indicate a persistent issue.";
                    OnConnectionStatus?.Invoke(warning);
                }

                var delay = CalcBackoff(_reconnectAttempts);
                var reconnectMessage = $"Reconnecting in {delay.TotalSeconds:F1} seconds... (attempt #{_reconnectAttempts})";
                OnConnectionStatus?.Invoke(reconnectMessage);

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
                    OnConnectionStatus?.Invoke(errorMessage);
                }
            }
        }

        private static (CloseSeverity severity, string message) DescribeClose(int? code, string? reason)
        {
            var suffix = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}";
            
            return code switch
            {
                1000 => (CloseSeverity.Info, "Connection closed normally (1000)." + suffix),
                1001 => (CloseSeverity.Warn, "Server unavailable or intentionally closed the connection (1001)." + suffix),
                1002 => (CloseSeverity.Error, "Protocol error occurred (1002)." + suffix),
                1003 => (CloseSeverity.Error, "Invalid message type received (1003)." + suffix),
                1005 => (CloseSeverity.Warn, "Connection was closed without a close frame (1005)." + suffix),
                1007 => (CloseSeverity.Error, "Invalid payload data in the WebSocket frame (1007)." + suffix),
                1008 => (CloseSeverity.Warn, "Policy violation (1008). Possibly due to rate limits or access rules." + suffix),
                1009 => (CloseSeverity.Warn, "Message too large (1009)." + suffix),
                1010 => (CloseSeverity.Error, "Mandatory WebSocket extension is missing (1010)." + suffix),
                1011 => (CloseSeverity.Error, "Internal server error (1011)." + suffix),
                _ => (CloseSeverity.Warn, $"Connection closed with code {code}." + suffix)
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
            if (data.Warnings is { Count: > 0 } && OnWarning2 is not null)
            {
                await OnWarning2.Invoke(data.Warnings, message);
            }
            if (data.Type == null && data.Error != null)
            {
                if (data.Error == "slowDown" || data.Error == "tooBusy")
                {
                    var rateLimitMessage = data.Error == "slowDown" 
                        ? "Rate limit warning: Server requests to slow down. Reduce request frequency to avoid connection issues." 
                        : "Rate limit warning: Server is too busy. Consider implementing exponential backoff or reducing load.";
                    
                    if (OnWarning is not null)
                        await OnWarning.Invoke($"RATE_LIMIT: {data.Error}", rateLimitMessage);
                    
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