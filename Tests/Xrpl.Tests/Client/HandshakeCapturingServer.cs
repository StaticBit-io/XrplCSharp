using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Tests.MockRippled;

namespace Xrpl.Tests
{
    /// <summary>
    /// Minimal WebSocket server that captures the raw HTTP upgrade request of the first
    /// client and then completes a valid handshake, so the client stays connected.
    /// Used to assert which headers the SDK puts on the WebSocket handshake.
    /// </summary>
    internal sealed class HandshakeCapturingServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<string> _handshake =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HandshakeCapturingServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }

        public int Port { get; }

        public string Url => $"ws://127.0.0.1:{Port}/";

        /// <summary>Raw text of the client's HTTP upgrade request, including all headers.</summary>
        public Task<string> HandshakeRequest => _handshake.Task;

        public async Task<string> WaitForHandshakeAsync(TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(_handshake.Task, Task.Delay(timeout, _cts.Token))
                .ConfigureAwait(false);

            if (completed != _handshake.Task)
            {
                throw new TimeoutException($"No WebSocket handshake received within {timeout.TotalSeconds:F0}s.");
            }

            return await _handshake.Task.ConfigureAwait(false);
        }

        private async Task AcceptAsync()
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();

                string request = await ReadUntilHeadersEndAsync(stream).ConfigureAwait(false);
                _handshake.TrySetResult(request);

                string key = Helpers.GetHandshakeRequestKey(request);
                byte[] response = Encoding.ASCII.GetBytes(Helpers.GetHandshakeResponse(Helpers.HashKey(key)));
                await stream.WriteAsync(response, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);

                // Hold the connection open until the test disposes the server.
                await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _handshake.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _handshake.TrySetException(ex);
            }
        }

        private async Task<string> ReadUntilHeadersEndAsync(NetworkStream stream)
        {
            byte[] buffer = new byte[4096];
            StringBuilder request = new StringBuilder();

            while (true)
            {
                int read = await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));

                if (request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return request.ToString();
        }

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
            }
            catch
            {
                // best effort
            }

            try
            {
                _listener.Stop();
            }
            catch
            {
                // best effort
            }

            _cts.Dispose();
        }
    }
}
