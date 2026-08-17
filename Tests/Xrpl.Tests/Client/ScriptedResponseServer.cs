using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Xrpl.Tests
{
    /// <summary>
    /// WebSocket server that answers every request with a response body the test wrote itself,
    /// byte for byte.
    /// </summary>
    /// <remarks>
    /// The other mock servers here either record requests or generate filler payloads. Neither can
    /// show what this one is for: that the bytes a node sent survive the trip to the caller
    /// unchanged — including whitespace the client never normalizes and members no model knows.
    /// Only the id is substituted, because the client matches responses on it.
    /// </remarks>
    internal sealed class ScriptedResponseServer : WebSocketTestServerBase
    {
        private readonly string _envelope;

        /// <param name="envelope">
        /// The full response, with <c>__ID__</c> where the request's id has to be echoed back.
        /// </param>
        public ScriptedResponseServer(string envelope)
        {
            _envelope = envelope;
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

                byte[] response = Encoding.UTF8.GetBytes(_envelope.Replace("__ID__", ExtractId(request)));
                await WriteFragmentedMessageAsync(stream, response, fragments: 1).ConfigureAwait(false);
            }
        }

        /// <summary>Echoes the request's id back verbatim, quotes included.</summary>
        private static string ExtractId(string request)
        {
            using JsonDocument document = JsonDocument.Parse(request);
            return document.RootElement.TryGetProperty("id", out JsonElement id)
                ? id.GetRawText()
                : "null";
        }
    }
}
