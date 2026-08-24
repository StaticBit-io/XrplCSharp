using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that answers ordinary requests but never answers a <c>ping</c>.
    /// </summary>
    /// <remarks>
    /// The health check treats a connection with no inbound traffic past
    /// <c>InactivityTimeout</c> as dead and hands it to the fast-reconnect path. Reaching that from
    /// a test needs a peer that stays connected and stays quiet, which the shared mock cannot be:
    /// it answers <c>ping</c> itself, and its pong refreshes the activity clock on every check, so
    /// the timeout never comes however low it is set. Answering everything else is what keeps the
    /// connection up long enough for the silence to matter.
    /// </remarks>
    internal sealed class SilentOnPingServer : WebSocketTestServerBase
    {
        private const string ServerInfoEnvelope =
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{\"info\":" +
            "{\"build_version\":\"test-mock\",\"complete_ledgers\":\"1-1\",\"server_state\":\"full\"}}}";

        public SilentOnPingServer()
        {
            StartAccepting();
        }

        protected override async Task ServeAsync(NetworkStream stream)
        {
            while (!Token.IsCancellationRequested)
            {
                string request = await ReadTextFrameAsync(stream).ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                using JsonDocument document = JsonDocument.Parse(request);
                string command = document.RootElement.TryGetProperty("command", out JsonElement value)
                    ? value.GetString()
                    : null;

                if (command == "ping")
                {
                    continue;
                }

                string id = document.RootElement.TryGetProperty("id", out JsonElement requestId)
                    ? requestId.GetRawText()
                    : "null";

                byte[] response = Encoding.UTF8.GetBytes(ServerInfoEnvelope.Replace("__ID__", id));
                await WriteFragmentedMessageAsync(stream, response, fragments: 1).ConfigureAwait(false);
            }
        }
    }
}
