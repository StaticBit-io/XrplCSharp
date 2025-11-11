

using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.Client
{
    //credit: https://github.com/Badiboy/WebSocketWrapper/blob/master/WebSocketWrapper.cs
    public class WebSocketClient : IDisposable
    {

        private const int ReceiveChunkSize = 1048576;
        private const int SendChunkSize = 1048576;

        private enum CloseSeverity { Info, Warn, Error }

        /// <summary>
        /// Returns a human-readable description and severity level for a given WebSocket close code.
        /// </summary>
        private static (CloseSeverity severity, string message) DescribeClose(WebSocketCloseStatus? code, string? reason)
        {
            var suffix = string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}";

            return code switch
            {
                WebSocketCloseStatus.NormalClosure =>
                    (CloseSeverity.Info, "Connection closed normally (1000)." + suffix),

                WebSocketCloseStatus.EndpointUnavailable =>
                    (CloseSeverity.Warn, "Server unavailable or intentionally closed the connection (1001)." + suffix),

                WebSocketCloseStatus.ProtocolError =>
                    (CloseSeverity.Error, "Protocol error occurred (1002)." + suffix),

                WebSocketCloseStatus.InvalidMessageType =>
                    (CloseSeverity.Error, "Invalid message type received (1003)." + suffix),

                WebSocketCloseStatus.Empty =>
                    (CloseSeverity.Warn, "Connection was closed without a close frame (1005)." + suffix),

                WebSocketCloseStatus.InvalidPayloadData =>
                    (CloseSeverity.Error, "Invalid payload data in the WebSocket frame (1007)." + suffix),

                WebSocketCloseStatus.PolicyViolation =>
                    (CloseSeverity.Warn, "Policy violation (1008). Possibly due to rate limits or access rules." + suffix),

                WebSocketCloseStatus.MessageTooBig =>
                    (CloseSeverity.Warn, "Message too large (1009)." + suffix),

                WebSocketCloseStatus.MandatoryExtension =>
                    (CloseSeverity.Error, "Mandatory WebSocket extension is missing (1010)." + suffix),

                WebSocketCloseStatus.InternalServerError =>
                    (CloseSeverity.Error, "Internal server error (1011)." + suffix),

                _ =>
                    (CloseSeverity.Warn, $"Connection closed with code {(int?)(code ?? 0)}." + suffix)
            };
        }

        /// <summary>
        /// Determines whether the client should attempt to reconnect based on the WebSocket close code.
        /// </summary>
        private static bool ShouldReconnect(WebSocketCloseStatus? code) => code switch
        {
            null => true,
            WebSocketCloseStatus.NormalClosure => false,
            WebSocketCloseStatus.ProtocolError => false,
            WebSocketCloseStatus.InvalidMessageType => false,
            WebSocketCloseStatus.InvalidPayloadData => false,
            WebSocketCloseStatus.MandatoryExtension => false,

            WebSocketCloseStatus.Empty => true,
            WebSocketCloseStatus.EndpointUnavailable => true,
            WebSocketCloseStatus.PolicyViolation => true,
            WebSocketCloseStatus.MessageTooBig => true,
            WebSocketCloseStatus.InternalServerError => true,

            _ => true
        };

        private ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private Func<WebSocketClient, Task> _onConnected;
        private Func<Exception, WebSocketClient, Task> _onConnectionError;
        private Func<byte[], WebSocketClient, Task> _onMessageBinary;
        private Func<string, WebSocketClient, Task> _onMessageString;
        private Func<Exception, WebSocketClient, Task> _onError;
        private Func<WebSocketCloseStatus?, string?, WebSocketClient, Task> _onDisconnected;
        private Func<WebSocketClient, Task> _onClosed;

        protected WebSocketClient(string uri)
        {
            _uri = new Uri(uri);
            _cancellationToken = _cancellationTokenSource.Token;
        }
        /// <summary> cancel work </summary>
        public void Cancel() => _cancellationTokenSource.Cancel();

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="uri">The URI of the WebSocket server.</param>
        /// <returns>Instance of the created WebSocketWrapper</returns>
        public static WebSocketClient Create(string uri)
        {
            return new WebSocketClient(uri);
        }

        /// <summary>
        /// Connects to the WebSocket server.
        /// </summary>
        /// <returns>Self</returns>
        public async Task<WebSocketClient> Connect()
        {
            if (_ws == null)
            {
                _ws = new ClientWebSocket();
                if (!OperatingSystem.IsBrowser())
                {
                    _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                }
            }

            await ConnectAsync();
            return this;
        }

        /// <summary>
        /// Disconnects from the WebSocket server.
        /// </summary>
        /// <returns>Self</returns>
        internal WebSocketClient Disconnect()
        {
            DisconnectAsync();
            return this;
        }

        /// <summary>
        /// Get the current state of the WebSocket client.
        /// </summary>
        public WebSocketState State
        {
            get
            {
                if (_ws == null)
                    return WebSocketState.None;

                return _ws.State;
            }
        }

        /// <summary>
        /// Set the Action to call when the connection has been established.
        /// </summary>
        /// <param name="onConnect">The Action to call</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnConnect(Func<WebSocketClient, Task> onConnect)
        {
            _onConnected = onConnect;
            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection fails.
        /// </summary>
        /// <param name="onConnectionError">The Action to call</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnConnectionError(Func<Exception, WebSocketClient, Task> onConnectionError)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return this;
            _onConnectionError = onConnectionError;
            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection fails.
        /// </summary>
        /// <param name="onConnectionError">The Action to call</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnError(Func<Exception, WebSocketClient, Task> onError)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return this;
            _onError = onError;
            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection has been terminated.
        /// </summary>
        /// <param name="onDisconnect">The Action to call</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnDisconnect(Func<WebSocketCloseStatus?, string?, WebSocketClient, Task> onDisconnect)
        {
            _onDisconnected = onDisconnect;
            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection has been closed.
        /// </summary>
        /// <param name="onClosed">The Action to call</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnClosed(Func<WebSocketClient, Task> onClosed)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return this;
            _onClosed = onClosed;
            return this;
        }

        /// <summary>
        /// Set the Action to call when a messages has been received.
        /// </summary>
        /// <param name="onMessage">The Action to call.</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnBinaryMessage(Func<byte[], WebSocketClient, Task> onMessage)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return this;
            _onMessageBinary = onMessage;
            return this;
        }

        /// <summary>
        /// Set the Action to call when a messages has been received.
        /// </summary>
        /// <param name="onMessage">The Action to call.</param>
        /// <returns>Self</returns>
        internal WebSocketClient OnMessageReceived(Func<string, WebSocketClient, Task> onMessage)
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return this;
            _onMessageString = onMessage;
            return this;
        }

        /// <summary>
        /// Send a UTF8 string to the WebSocket server.
        /// </summary>
        /// <param name="message">The message to send</param>
        public void SendMessage(string message)
        {
            SendMessageAsync(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Send a byte array to the WebSocket server.
        /// </summary>
        /// <param name="message">The data to send</param>
        private async void SendMessageAsync(byte[] message)
        {
            if (_ws is null)
                return;
            if (_ws.State != WebSocketState.Open)
            {
                try
                {
                    _ = Connect();
                }
                catch (Exception e)
                {
                    throw new Exception("Connection is not open.");
                }
            }

            var messagesCount = (int)Math.Ceiling((double)message.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > message.Length)
                {
                    count = message.Length - offset;
                }

                try
                {
                    await _ws.SendAsync(new ArraySegment<byte>(message, offset, count), WebSocketMessageType.Binary, lastMessage, _cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    return;
                }
            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                await _ws.ConnectAsync(_uri, _cancellationToken);
                CallOnConnected();
                StartListen();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                if (!IsDisposed)
                    Dispose();
                return;
            }
            catch (Exception e)
            {

                _ws?.Dispose();
                _ws = null;
                CallOnConnectionError(e);
            }
        }

        private async void DisconnectAsync()
        {
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open)
                    try
                    {
                        await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        CallOnError(e);
                    }
                Dispose();
                _ws = null;
                CallOnDisconnected(WebSocketCloseStatus.NormalClosure, "Client initiated disconnect");
            }
        }

        private async void StartListen()
        {
            var buffer = new byte[ReceiveChunkSize];

            try
            {
                while (_ws is { State: WebSocketState.Open })
                {
                    byte[] byteResult = Array.Empty<byte>();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            var closeStatus = result.CloseStatus;
                            var closeDescription = result.CloseStatusDescription;
                            
                            var (severity, message) = DescribeClose(closeStatus, closeDescription);
                            
                            _onClosed?.Invoke(this);
                            CallOnDisconnected(closeStatus, closeDescription);
                            Dispose();
                            _ws = null;
                            return;
                        }
                        else
                        {
                            byteResult = byteResult.Concat(buffer.Take(result.Count)).ToArray();
                        }

                    } while (!result.EndOfMessage);

                    CallOnMessage(byteResult);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _onConnectionError?.Invoke(e, this);
                Disconnect();
            }
        }

        private void CallOnMessage(byte[] result)
        {
            _onMessageBinary?.Invoke(result, this);
            _onMessageString?.Invoke(Encoding.UTF8.GetString(result), this);
        }


        private void CallOnDisconnected(WebSocketCloseStatus? closeStatus = null, string? closeDescription = null)
        {
            _onDisconnected?.Invoke(closeStatus, closeDescription, this);
        }

        private void CallOnConnected()
        {
            _onConnected?.Invoke(this);
        }

        private void CallOnConnectionError(Exception e)
        {
            _onConnectionError?.Invoke(e, this);
        }

        private void CallOnError(Exception e)
        {
            _onError?.Invoke(e, this);
        }

        public bool IsDisposed;
        public void Dispose()
        {
            if(_cancellationTokenSource?.IsCancellationRequested==true)
                _cancellationTokenSource.Cancel();
            IsDisposed = true;
            _ws?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}