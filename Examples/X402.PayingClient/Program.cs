using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Xrpl.X402.Examples.PayingClient;

// Configuration comes from appsettings.json (the "X402" section); any value can be overridden by
// an environment variable. See appsettings.json for the full list of knobs.
try
{
    PayingClientOptions options = LoadOptions();

    options.RippledWsUrl = Environment.GetEnvironmentVariable("RIPPLED_WS") ?? options.RippledWsUrl;
    options.ResourceUrl = Environment.GetEnvironmentVariable("RESOURCE_URL") ?? options.ResourceUrl;
    options.PayerSeed = Environment.GetEnvironmentVariable("PAYER_SEED") ?? options.PayerSeed;
    options.Network = Environment.GetEnvironmentVariable("NETWORK") ?? options.Network;

    if (string.IsNullOrWhiteSpace(options.PayerSeed))
        throw new InvalidOperationException(
            "X402:PayerSeed is not configured. Set it in appsettings.json (the X402 section) " +
            "or via the PAYER_SEED environment variable, then run again.");

    Console.WriteLine($"[client] fetching {options.ResourceUrl} (paying any 402 automatically)...");

    PaidResult result = await PayingClient.FetchAsync(options);

    Console.WriteLine($"[client] body    = {result.Body}");
    Console.WriteLine($"[client] settled = {result.Settled}");
    Console.WriteLine($"[client] tx      = {result.TxHash}");
    Console.WriteLine($"[client] payer   = {result.Payer}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[client] error: {ex.Message}");
}
finally
{
    // Keep the window open when launched interactively (e.g. Visual Studio F5), but never block
    // when the output is captured/piped (CI, shell redirection).
    if (!Console.IsOutputRedirected)
    {
        Console.WriteLine();
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
}

static PayingClientOptions LoadOptions()
{
    PayingClientOptions options = new();
    string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(path))
        return options;

    using JsonDocument doc = JsonDocument.Parse(
        File.ReadAllText(path),
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    if (!doc.RootElement.TryGetProperty("X402", out JsonElement x))
        return options;

    if (x.TryGetProperty("RippledWsUrl", out JsonElement v1) && v1.ValueKind == JsonValueKind.String)
        options.RippledWsUrl = v1.GetString()!;
    if (x.TryGetProperty("ResourceUrl", out JsonElement v2) && v2.ValueKind == JsonValueKind.String)
        options.ResourceUrl = v2.GetString()!;
    if (x.TryGetProperty("PayerSeed", out JsonElement v3) && v3.ValueKind == JsonValueKind.String)
        options.PayerSeed = v3.GetString()!;
    if (x.TryGetProperty("Network", out JsonElement v4) && v4.ValueKind == JsonValueKind.String)
        options.Network = v4.GetString()!;
    if (x.TryGetProperty("MaxAmountDrops", out JsonElement v5) && v5.ValueKind == JsonValueKind.Number && v5.TryGetUInt64(out ulong drops))
        options.MaxAmountDrops = drops;

    // Per-issuer IOU/RLUSD caps: { "<issuerAddress>": <decimal cap>, ... }. Required to pay any IOU.
    if (x.TryGetProperty("IouValueCaps", out JsonElement v6) && v6.ValueKind == JsonValueKind.Object)
    {
        Dictionary<string, decimal> caps = new();
        foreach (JsonProperty cap in v6.EnumerateObject())
        {
            if (cap.Value.ValueKind == JsonValueKind.Number && cap.Value.TryGetDecimal(out decimal value))
                caps[cap.Name] = value;
        }
        if (caps.Count > 0)
            options.IouValueCaps = caps;
    }

    return options;
}
