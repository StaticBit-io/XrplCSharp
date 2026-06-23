using System;
using System.Threading.Tasks;

using Xrpl.X402.Examples.PayingClient;

// Example entry point. Configure via environment variables, then `dotnet run`.
// Points an x402-aware HttpClient at a payment-protected resource and pays its 402 automatically.
PayingClientOptions options = new()
{
    ResourceUrl = Environment.GetEnvironmentVariable("RESOURCE_URL") ?? "http://127.0.0.1:5402/paid",
    PayerSeed = Environment.GetEnvironmentVariable("PAYER_SEED") ?? "",
    RippledWsUrl = Environment.GetEnvironmentVariable("RIPPLED_WS") ?? "ws://localhost:6006",
    Network = Environment.GetEnvironmentVariable("NETWORK") ?? "xrpl:1",
};

if (string.IsNullOrWhiteSpace(options.PayerSeed))
{
    Console.Error.WriteLine("Set PAYER_SEED to the seed of a funded XRPL wallet before running.");
    return 1;
}

Console.WriteLine($"[client] fetching {options.ResourceUrl} (paying any 402 automatically)...");

PaidResult result = await PayingClient.FetchAsync(options);

Console.WriteLine($"[client] body    = {result.Body}");
Console.WriteLine($"[client] settled = {result.Settled}");
Console.WriteLine($"[client] tx      = {result.TxHash}");
Console.WriteLine($"[client] payer   = {result.Payer}");

return result.Settled ? 0 : 2;
