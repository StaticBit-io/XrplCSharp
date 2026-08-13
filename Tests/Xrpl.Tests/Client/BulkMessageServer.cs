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
    /// Minimal WebSocket server that completes a handshake and then pushes a fixed number of
    /// large text messages, each split into a controlled number of WebSocket continuation
    /// frames. Fragmenting at the protocol level (rather than relying on how the socket happens
    /// to slice the stream) makes the number of client-side receive chunks per message exact and
    /// reproducible, which is what the assembly path is sensitive to.
    /// </summary>
    internal sealed class BulkMessageServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly int _messageCount;
        private readonly int _fragments;
        private readonly int[] _lengthCycle;
        private readonly byte[] _payload;
        private readonly TaskCompletionSource<int> _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <param name="messageCount">How many messages to push once the client connects.</param>
        /// <param name="payloadBytes">Size of the longest message payload, in bytes.</param>
        /// <param name="fragments">
        /// Number of WebSocket frames each message is split into; the client sees exactly this
        /// many receive chunks per message.
        /// </param>
        /// <param name="lengthCycle">
        /// Optional cycle of message lengths, each a prefix of the full payload. Lets a test mix
        /// long and short messages on one connection, which is what exposes stale bytes left in a
        /// reused assembly buffer. Defaults to every message being the full payload.
        /// </param>
        public BulkMessageServer(int messageCount, int payloadBytes, int fragments, int[]? lengthCycle = null)
        {
            _messageCount = messageCount;
            _fragments = Math.Max(1, fragments);
            _payload = BuildPayload(payloadBytes);
            _lengthCycle = lengthCycle is { Length: > 0 } ? lengthCycle : new[] { _payload.Length };

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }

        public int Port { get; }

        public string Url => "ws://127.0.0.1:" + Port + "/";

        /// <summary>Payload every message carries, as the client should see it.</summary>
        public string PayloadText => Encoding.UTF8.GetString(_payload);

        public int PayloadBytes => _payload.Length;

        /// <summary>Completes with the number of messages written once the server is done sending.</summary>
        public Task<int> SendCompleted => _finished.Task;

        /// <summary>
        /// Builds an ASCII payload shaped like a paged rippled response, so the byte content is
        /// non-uniform and any accidental truncation during assembly is visible.
        /// </summary>
        private static byte[] BuildPayload(int payloadBytes)
        {
            StringBuilder builder = new StringBuilder(payloadBytes + 64);
            builder.Append("{\"id\":1,\"status\":\"success\",\"type\":\"response\",\"result\":{\"state\":[");

            int index = 0;
            while (builder.Length < payloadBytes - 80)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"i\":").Append(index).Append(",\"d\":\"");
                builder.Append((char)('A' + (index % 26)), 48);
                builder.Append("\"}");
                index++;
            }

            builder.Append("]}}");

            while (builder.Length < payloadBytes)
            {
                builder.Append(' ');
            }

            return Encoding.UTF8.GetBytes(builder.ToString(0, payloadBytes));
        }

        private async Task AcceptAsync()
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();

                string request = await ReadUntilHeadersEndAsync(stream).ConfigureAwait(false);
                string key = Helpers.GetHandshakeRequestKey(request);
                byte[] response = Encoding.ASCII.GetBytes(Helpers.GetHandshakeResponse(Helpers.HashKey(key)));
                await stream.WriteAsync(response, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);

                // Wait for the client's go-ahead before pushing anything, so no message can land
                // before the caller has opened its measurement window.
                byte[] goAhead = new byte[256];
                if (await stream.ReadAsync(goAhead, _cts.Token).ConfigureAwait(false) == 0)
                {
                    _finished.TrySetResult(0);
                    return;
                }

                // Drain and discard whatever the client sends afterwards (keep-alive pings, close
                // frames) so its socket never blocks on a full send window.
                _ = DrainAsync(stream);

                for (int i = 0; i < _messageCount; i++)
                {
                    int messageLength = _lengthCycle[i % _lengthCycle.Length];
                    int fragmentBytes = (messageLength + _fragments - 1) / _fragments;

                    for (int fragment = 0; fragment < _fragments; fragment++)
                    {
                        // Ceil division can leave trailing frames past the end; they are sent empty
                        // so the frame count stays exactly as requested.
                        int offset = Math.Min(fragment * fragmentBytes, messageLength);
                        int length = Math.Min(fragmentBytes, messageLength - offset);
                        bool isFirst = fragment == 0;
                        bool isLast = fragment == _fragments - 1;

                        await stream.WriteAsync(BuildFrameHeader(length, isFirst, isLast), _cts.Token)
                            .ConfigureAwait(false);
                        await stream.WriteAsync(_payload.AsMemory(offset, length), _cts.Token)
                            .ConfigureAwait(false);
                        await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                    }
                }

                _finished.TrySetResult(_messageCount);

                await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _finished.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _finished.TrySetException(ex);
            }
        }

        private async Task DrainAsync(NetworkStream stream)
        {
            byte[] sink = new byte[4096];

            try
            {
                while (await stream.ReadAsync(sink, _cts.Token).ConfigureAwait(false) > 0)
                {
                }
            }
            catch
            {
                // The connection going away is the normal end of this loop.
            }
        }

        /// <summary>
        /// Unmasked server-to-client frame header. The first frame of a message carries the text
        /// opcode (0x1), every following frame carries continuation (0x0); FIN is set on the last.
        /// </summary>
        private static byte[] BuildFrameHeader(int payloadLength, bool isFirst, bool isLast)
        {
            byte first = (byte)((isLast ? 0x80 : 0x00) | (isFirst ? 0x01 : 0x00));

            if (payloadLength <= 125)
            {
                return new byte[] { first, (byte)payloadLength };
            }

            if (payloadLength <= ushort.MaxValue)
            {
                return new byte[]
                {
                    first,
                    126,
                    (byte)(payloadLength >> 8),
                    (byte)payloadLength
                };
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
