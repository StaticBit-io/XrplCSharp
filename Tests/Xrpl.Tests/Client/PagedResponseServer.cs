using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Tests.MockRippled;

namespace Xrpl.Tests
{
    /// <summary>
    /// Minimal WebSocket server that answers every client request with a large, paged
    /// rippled-shaped response echoing the request id. Each response is split into a controlled
    /// number of WebSocket continuation frames, so the client assembles it from exactly that many
    /// receive chunks — which is what a multi-megabyte <c>ledger_data</c> page looks like over a
    /// real link.
    /// </summary>
    internal sealed class PagedResponseServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly int _fragments;
        private readonly string _resultBody;
        private int _served;

        /// <param name="approximatePayloadBytes">Target size of each response, in bytes.</param>
        /// <param name="fragments">Number of WebSocket frames each response is split into.</param>
        public PagedResponseServer(int approximatePayloadBytes, int fragments)
        {
            _fragments = Math.Max(1, fragments);
            _resultBody = BuildResultBody(approximatePayloadBytes);

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }

        public int Port { get; }

        public string Url => "ws://127.0.0.1:" + Port + "/";

        /// <summary>Number of requests answered so far.</summary>
        public int Served => Volatile.Read(ref _served);

        /// <summary>
        /// Body of the <c>result</c> object: a binary-form <c>ledger_data</c> page, i.e. a list of
        /// {data, index} pairs, which is what a bulk ledger crawl actually receives.
        /// </summary>
        private static string BuildResultBody(int approximatePayloadBytes)
        {
            StringBuilder builder = new StringBuilder(approximatePayloadBytes + 256);
            builder.Append("{\"ledger_hash\":\"842B57C1CC0613299A686D3E9F310EC0422C84D3911E5056389AA7E5808A93C8\",");
            builder.Append("\"ledger_index\":\"96000000\",\"validated\":true,\"state\":[");

            int index = 0;
            while (builder.Length < approximatePayloadBytes)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"data\":\"");
                AppendHex(builder, index, 900);
                builder.Append("\",\"index\":\"");
                AppendHex(builder, index + 7, 64);
                builder.Append("\"}");
                index++;
            }

            builder.Append("],\"marker\":\"");
            AppendHex(builder, index, 64);
            builder.Append("\"}");

            return builder.ToString();
        }

        private static void AppendHex(StringBuilder builder, int seed, int length)
        {
            const string Digits = "0123456789ABCDEF";
            int state = (seed * 1103515245) ^ 0x5F3A;

            for (int i = 0; i < length; i++)
            {
                state = (state * 1103515245) + 12345;
                builder.Append(Digits[(state >> 16) & 0xF]);
            }
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

                while (!_cts.IsCancellationRequested)
                {
                    string? message = await ReadTextFrameAsync(stream).ConfigureAwait(false);
                    if (message == null)
                    {
                        return;
                    }

                    string id = ExtractId(message);
                    await WriteResponseAsync(stream, id).ConfigureAwait(false);
                    Interlocked.Increment(ref _served);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // The client going away is the normal end of this loop.
            }
        }

        /// <summary>Pulls the JSON string value of the request's "id" property.</summary>
        private static string ExtractId(string message)
        {
            int keyIndex = message.IndexOf("\"id\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return "\"0\"";
            }

            int colon = message.IndexOf(':', keyIndex);
            if (colon < 0)
            {
                return "\"0\"";
            }

            int start = colon + 1;
            while (start < message.Length && char.IsWhiteSpace(message[start]))
            {
                start++;
            }

            if (start < message.Length && message[start] == '"')
            {
                int end = message.IndexOf('"', start + 1);
                return end < 0 ? "\"0\"" : message.Substring(start, end - start + 1);
            }

            int stop = start;
            while (stop < message.Length && message[stop] != ',' && message[stop] != '}')
            {
                stop++;
            }

            return message.Substring(start, stop - start).Trim();
        }

        private async Task WriteResponseAsync(NetworkStream stream, string id)
        {
            string envelope = "{\"id\":" + id + ",\"status\":\"success\",\"type\":\"response\",\"result\":" +
                              _resultBody + "}";
            byte[] payload = Encoding.UTF8.GetBytes(envelope);
            int fragmentBytes = (payload.Length + _fragments - 1) / _fragments;

            for (int fragment = 0; fragment < _fragments; fragment++)
            {
                // Ceil division can leave trailing frames past the end; they are sent empty
                // so the frame count stays exactly as requested.
                int offset = Math.Min(fragment * fragmentBytes, payload.Length);
                int length = Math.Min(fragmentBytes, payload.Length - offset);
                bool isFirst = fragment == 0;
                bool isLast = fragment == _fragments - 1;

                await stream.WriteAsync(BuildFrameHeader(length, isFirst, isLast), _cts.Token)
                    .ConfigureAwait(false);
                await stream.WriteAsync(payload.AsMemory(offset, length), _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
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
        /// Reads one client frame. Returns the decoded text of the first text frame seen, or null
        /// once the peer closes. Control frames other than Close are skipped.
        /// </summary>
        private async Task<string?> ReadTextFrameAsync(NetworkStream stream)
        {
            while (true)
            {
                byte[] head = new byte[2];
                if (!await ReadExactAsync(stream, head, 2).ConfigureAwait(false))
                {
                    return null;
                }

                int opcode = head[0] & 0x0F;
                bool masked = (head[1] & 0x80) != 0;
                long length = head[1] & 0x7F;

                if (length == 126)
                {
                    byte[] extended = new byte[2];
                    if (!await ReadExactAsync(stream, extended, 2).ConfigureAwait(false))
                    {
                        return null;
                    }

                    length = BinaryPrimitives.ReadUInt16BigEndian(extended);
                }
                else if (length == 127)
                {
                    byte[] extended = new byte[8];
                    if (!await ReadExactAsync(stream, extended, 8).ConfigureAwait(false))
                    {
                        return null;
                    }

                    length = (long)BinaryPrimitives.ReadUInt64BigEndian(extended);
                }

                byte[] mask = new byte[4];
                if (masked && !await ReadExactAsync(stream, mask, 4).ConfigureAwait(false))
                {
                    return null;
                }

                byte[] payload = new byte[length];
                if (length > 0 && !await ReadExactAsync(stream, payload, (int)length).ConfigureAwait(false))
                {
                    return null;
                }

                if (masked)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] ^= mask[i % 4];
                    }
                }

                if (opcode == 0x8)
                {
                    return null;
                }

                if (opcode == 0x1 || opcode == 0x2)
                {
                    return Encoding.UTF8.GetString(payload);
                }
            }
        }

        private async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int read = 0;

            while (read < count)
            {
                int chunk = await stream.ReadAsync(buffer.AsMemory(read, count - read), _cts.Token)
                    .ConfigureAwait(false);
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
