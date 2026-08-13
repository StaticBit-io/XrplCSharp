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
    /// Shared plumbing for the raw WebSocket servers the client tests drive: a loopback listener,
    /// the HTTP upgrade handshake, frame framing and disposal. The framing in particular lives here
    /// on purpose — it used to be copied per server, and the copies drifted.
    /// </summary>
    internal abstract class WebSocketTestServerBase : IDisposable
    {
        private readonly TcpListener _listener;
        private Exception? _fault;

        protected WebSocketTestServerBase()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        /// <summary>Token cancelled when the server is disposed.</summary>
        protected CancellationToken Token => Cts.Token;

        protected CancellationTokenSource Cts { get; } = new CancellationTokenSource();

        public int Port { get; }

        public string Url => "ws://127.0.0.1:" + Port + "/";

        /// <summary>
        /// Starts accepting. Call at the end of the derived constructor, once its own state is set:
        /// the accept loop runs on the thread pool and may reach <see cref="ServeAsync"/> at any time.
        /// </summary>
        protected void StartAccepting()
        {
            _ = AcceptAsync();
        }

        /// <summary>Serves one connected client; the handshake has already completed.</summary>
        protected abstract Task ServeAsync(NetworkStream stream);

        /// <summary>
        /// The exception that ended the accept loop, if it ended badly. Recorded rather than
        /// swallowed so a broken server shows up as itself instead of as the caller's timeout.
        /// </summary>
        public Exception? Fault => Volatile.Read(ref _fault);

        /// <summary>Called when the accept loop ends with an exception the server did not expect.</summary>
        protected virtual void OnFaulted(Exception error)
        {
            Volatile.Write(ref _fault, error);
        }

        /// <summary>Called when the accept loop ends because the server was disposed.</summary>
        protected virtual void OnCancelled()
        {
        }

        private async Task AcceptAsync()
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(Token).ConfigureAwait(false);
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();

                string request = await ReadUntilHeadersEndAsync(stream).ConfigureAwait(false);
                string key = Helpers.GetHandshakeRequestKey(request);
                byte[] response = Encoding.ASCII.GetBytes(Helpers.GetHandshakeResponse(Helpers.HashKey(key)));
                await stream.WriteAsync(response, Token).ConfigureAwait(false);
                await stream.FlushAsync(Token).ConfigureAwait(false);

                await ServeAsync(stream).ConfigureAwait(false);

                // Hold the connection open until the test disposes the server.
                await Task.Delay(Timeout.InfiniteTimeSpan, Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                OnCancelled();
            }
            catch (Exception ex)
            {
                OnFaulted(ex);
            }
        }

        /// <summary>
        /// Unmasked server-to-client frame header. The first frame of a message carries the text
        /// opcode (0x1), every following frame carries continuation (0x0); FIN is set on the last.
        /// </summary>
        protected static byte[] BuildFrameHeader(int payloadLength, bool isFirst, bool isLast)
        {
            byte first = (byte)((isLast ? 0x80 : 0x00) | (isFirst ? 0x01 : 0x00));

            if (payloadLength <= 125)
            {
                return new byte[] { first, (byte)payloadLength };
            }

            if (payloadLength <= ushort.MaxValue)
            {
                return new byte[] { first, 126, (byte)(payloadLength >> 8), (byte)payloadLength };
            }

            return new byte[]
            {
                first,
                127,
                0, 0, 0, 0,
                (byte)(payloadLength >> 24),
                (byte)(payloadLength >> 16),
                (byte)(payloadLength >> 8),
                (byte)payloadLength
            };
        }

        /// <summary>
        /// Writes one message split into <paramref name="fragments"/> frames. Ceil division can
        /// leave trailing frames past the end of the payload; they are sent empty so the frame
        /// count is exactly what the caller asked for.
        /// </summary>
        protected async Task WriteFragmentedMessageAsync(NetworkStream stream, ReadOnlyMemory<byte> payload, int fragments)
        {
            int fragmentBytes = (payload.Length + fragments - 1) / fragments;

            for (int fragment = 0; fragment < fragments; fragment++)
            {
                int offset = Math.Min(fragment * fragmentBytes, payload.Length);
                int length = Math.Min(fragmentBytes, payload.Length - offset);
                bool isFirst = fragment == 0;
                bool isLast = fragment == fragments - 1;

                await stream.WriteAsync(BuildFrameHeader(length, isFirst, isLast), Token).ConfigureAwait(false);
                await stream.WriteAsync(payload.Slice(offset, length), Token).ConfigureAwait(false);
                await stream.FlushAsync(Token).ConfigureAwait(false);
            }
        }

        protected async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int read = 0;

            while (read < count)
            {
                int chunk = await stream.ReadAsync(buffer.AsMemory(read, count - read), Token).ConfigureAwait(false);
                if (chunk == 0)
                {
                    return false;
                }

                read += chunk;
            }

            return true;
        }

        private async Task<string> ReadUntilHeadersEndAsync(NetworkStream stream)
        {
            byte[] buffer = new byte[4096];
            StringBuilder request = new StringBuilder();

            while (true)
            {
                int read = await stream.ReadAsync(buffer, Token).ConfigureAwait(false);
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
                Cts.Cancel();
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

            Cts.Dispose();
        }
    }
}
