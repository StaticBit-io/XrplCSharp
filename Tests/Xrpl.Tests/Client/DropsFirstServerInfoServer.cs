using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that drops the connection the first time it is asked for
    /// <c>server_info</c>, and behaves normally on every connection after that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handshake succeeds, so a client reaches the point where it believes it is connected and
    /// sends its first request - and only then does the connection go away. That is the window in
    /// which reading the network id happens: a caller that reads it once, directly, fails the whole
    /// operation, while one that carries the read across a teardown recovers and completes.
    /// </para>
    /// <para>
    /// The shared mock cannot produce this: it answers every request on the connection it accepted,
    /// so the teardown never lands between "connected" and "first answer". Dropping the first
    /// request only, rather than every one, is what makes the recovery observable instead of just
    /// the failure.
    /// </para>
    /// </remarks>
    internal sealed class DropsFirstServerInfoServer : WebSocketTestServerBase
    {
        private const string ServerInfoEnvelope =
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"result\":{\"info\":" +
            "{\"build_version\":\"test-mock\",\"complete_ledgers\":\"1-1\",\"server_state\":\"full\"}}}";

        private int _dropsLeft = 1;

        public DropsFirstServerInfoServer()
        {
            StartAccepting();
        }

        /// <summary>The client reconnects after the drop, so the next connection has to be served.</summary>
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

                if (command == "server_info" && Interlocked.Decrement(ref _dropsLeft) >= 0)
                {
                    // Returning closes the socket without answering: the request the client is
                    // waiting for dies with the connection.
                    return;
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
