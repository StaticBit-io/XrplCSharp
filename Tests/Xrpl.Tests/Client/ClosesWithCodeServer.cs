using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that answers requests normally until told to stop, then closes the
    /// connection with a fixed status code.
    /// </summary>
    /// <remarks>
    /// The close codes the client does not reconnect after - 1002, 1003, 1007, 1010 - are the only
    /// way a connection ends with neither a consumer <c>Disconnect()</c> nor a reconnect sequence
    /// behind it, and no other test server can produce one: the shared mock never closes, and
    /// <c>CloseAfterHandshakeServer</c> sends a close frame carrying no code at all, which the
    /// client reads as "reconnect".
    /// </remarks>
    internal sealed class ClosesWithCodeServer : WebSocketTestServerBase
    {
        private const string ServerInfoEnvelope =
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{\"info\":" +
            "{\"build_version\":\"test-mock\",\"complete_ledgers\":\"1-1\",\"server_state\":\"full\"}}}";

        private readonly int _closeCode;
        private readonly SemaphoreSlim _closeNow = new SemaphoreSlim(initialCount: 0);

        public ClosesWithCodeServer(int closeCode)
        {
            _closeCode = closeCode;
            StartAccepting();
        }

        protected override bool ServesManyClients => true;

        /// <summary>Releases the serving loop to send the close frame.</summary>
        public void CloseNow() => _closeNow.Release();

        protected override async Task ServeAsync(NetworkStream stream)
        {
            Task closeRequested = _closeNow.WaitAsync(Token);

            while (!Token.IsCancellationRequested)
            {
                Task<string?> nextRequest = ReadTextFrameAsync(stream);
                Task first = await Task.WhenAny(nextRequest, closeRequested).ConfigureAwait(false);

                if (first == closeRequested)
                {
                    // FIN + close opcode, two payload bytes, the code big-endian.
                    byte[] close =
                    {
                        0x88, 0x02, (byte)(_closeCode >> 8), (byte)(_closeCode & 0xFF),
                    };

                    await stream.WriteAsync(close, Token).ConfigureAwait(false);
                    await stream.FlushAsync(Token).ConfigureAwait(false);
                    return;
                }

                string? request = await nextRequest.ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                using JsonDocument document = JsonDocument.Parse(request);
                string id = document.RootElement.TryGetProperty("id", out JsonElement requestId)
                    ? requestId.GetRawText()
                    : "null";

                byte[] response = Encoding.UTF8.GetBytes(ServerInfoEnvelope.Replace("__ID__", id));
                await WriteFragmentedMessageAsync(stream, response, fragments: 1).ConfigureAwait(false);
            }
        }
    }
}
