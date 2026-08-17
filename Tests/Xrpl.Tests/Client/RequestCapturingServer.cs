using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that records the raw text of every request it receives and answers each
    /// one with an empty success. Lets a test assert on what the client actually put on the wire,
    /// which is the only way to see fields the node would silently ignore.
    /// </summary>
    internal sealed class RequestCapturingServer : WebSocketTestServerBase
    {
        private readonly ConcurrentQueue<string> _requests = new ConcurrentQueue<string>();

        public RequestCapturingServer()
        {
            StartAccepting();
        }

        /// <summary>Every request seen so far, in arrival order.</summary>
        public IReadOnlyCollection<string> Requests => _requests;

        /// <summary>The last request whose <c>command</c> is <paramref name="command"/>.</summary>
        public string LastRequestFor(string command)
        {
            string found = null;
            foreach (string request in _requests)
            {
                using JsonDocument document = JsonDocument.Parse(request);
                if (document.RootElement.TryGetProperty("command", out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() == command)
                {
                    found = request;
                }
            }

            return found;
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

                _requests.Enqueue(request);

                string id = ExtractId(request);
                byte[] response = Encoding.UTF8.GetBytes(
                    "{\"id\":" + id + ",\"status\":\"success\",\"type\":\"response\",\"result\":{}}");

                await WriteFragmentedMessageAsync(stream, response, fragments: 1).ConfigureAwait(false);
            }
        }

        /// <summary>Echoes the request's id back verbatim, quotes included.</summary>
        private static string ExtractId(string request)
        {
            using JsonDocument document = JsonDocument.Parse(request);
            if (!document.RootElement.TryGetProperty("id", out JsonElement id))
            {
                return "\"0\"";
            }

            return id.ValueKind == JsonValueKind.String ? "\"" + id.GetString() + "\"" : id.ToString();
        }
    }
}
