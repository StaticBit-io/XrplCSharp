using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenerateEnums;

/// <summary>
/// Fetches server_definitions from a node, transport chosen by URL scheme:
/// ws/wss -> WebSocket, http/https -> JSON-RPC POST. Returns the parsed
/// Definitions (the "result" envelope is unwrapped by Definitions.ParseResponse).
/// </summary>
public static class ServerDefinitionsClient
{
    public static async Task<Definitions> FetchAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException($"Invalid URL: {url}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        string json = uri.Scheme switch
        {
            "ws" or "wss" => await FetchWebSocketAsync(uri, cts.Token),
            "http" or "https" => await FetchHttpAsync(uri, cts.Token),
            _ => throw new ArgumentException($"Unsupported URL scheme '{uri.Scheme}' (use ws/wss/http/https)"),
        };

        using JsonDocument doc = JsonDocument.Parse(json);
        return Definitions.ParseResponse(doc.RootElement);
    }

    private static async Task<string> FetchWebSocketAsync(Uri uri, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(uri, ct);

        byte[] request = Encoding.UTF8.GetBytes("{\"id\":1,\"command\":\"server_definitions\"}");
        await ws.SendAsync(request, WebSocketMessageType.Text, endOfMessage: true, ct);

        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("Node closed the connection before sending server_definitions.");
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        return sb.ToString();
    }

    private static async Task<string> FetchHttpAsync(Uri uri, CancellationToken ct)
    {
        using var http = new HttpClient();
        var body = new StringContent(
            "{\"method\":\"server_definitions\",\"params\":[{}]}",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(uri, body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
