using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that answers neither <c>ping</c> nor <c>ledger</c>, and answers everything
    /// else with the same <c>server_info</c> body.
    /// </summary>
    /// <remarks>
    /// Two silences, for two halves of one scenario. Not answering <c>ping</c> is what drives the
    /// health check to declare the connection dead and hand it to the fast-reconnect path - the
    /// shared mock answers pings itself, so its activity clock never runs out. Not answering
    /// <c>ledger</c> is what keeps a request in flight while that happens, so the sweep the
    /// reconnect performs has something to sweep. Answering everything else is what keeps the
    /// connection up long enough for either to matter.
    /// </remarks>
    internal sealed class SilentOnPingAndLedgerServer : WebSocketTestServerBase
    {
        private const string ServerInfoEnvelope =
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{\"info\":" +
            "{\"build_version\":\"test-mock\",\"complete_ledgers\":\"1-1\",\"server_state\":\"full\"}}}";

        public SilentOnPingAndLedgerServer()
        {
            StartAccepting();
        }

        /// <summary>The client reconnects when the health check gives up on the silence.</summary>
        protected override bool ServesManyClients => true;

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

                if (command == "ping" || command == "ledger")
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
