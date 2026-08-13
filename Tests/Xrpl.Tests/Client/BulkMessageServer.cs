using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that pushes a fixed number of large text messages once the client says go,
    /// each split into a controlled number of WebSocket continuation frames. Fragmenting at the
    /// protocol level (rather than relying on how the socket happens to slice the stream) makes the
    /// number of client-side receive chunks per message exact and reproducible, which is what the
    /// assembly path is sensitive to.
    /// </summary>
    internal sealed class BulkMessageServer : WebSocketTestServerBase
    {
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

            // Checked here rather than left to fail mid-send: the send loop runs detached, so a bad
            // length would only surface as the test's receive timeout minutes later.
            foreach (int length in _lengthCycle)
            {
                if (length < 0 || length > _payload.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(lengthCycle),
                        $"length {length} must be between 0 and the payload length {_payload.Length}");
                }
            }

            StartAccepting();
        }

        /// <summary>Payload every message is a prefix of, as the client should see it.</summary>
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

        protected override async Task ServeAsync(NetworkStream stream)
        {
            // Wait for the client's go-ahead before pushing anything, so no message can land
            // before the caller has opened its measurement window.
            byte[] goAhead = new byte[256];
            if (await stream.ReadAsync(goAhead, Token).ConfigureAwait(false) == 0)
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
                await WriteFragmentedMessageAsync(stream, _payload.AsMemory(0, messageLength), _fragments)
                    .ConfigureAwait(false);
            }

            _finished.TrySetResult(_messageCount);
        }

        protected override void OnCancelled() => _finished.TrySetCanceled();

        protected override void OnFaulted(Exception error) => _finished.TrySetException(error);

        private async Task DrainAsync(NetworkStream stream)
        {
            byte[] sink = new byte[4096];

            try
            {
                while (await stream.ReadAsync(sink, Token).ConfigureAwait(false) > 0)
                {
                }
            }
            catch
            {
                // The connection going away is the normal end of this loop.
            }
        }
    }
}
