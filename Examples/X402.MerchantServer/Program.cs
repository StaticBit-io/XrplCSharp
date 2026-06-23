using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

using Xrpl.X402.Examples.MerchantServer;

// Example entry point. Configure via environment variables, then `dotnet run`.
// Requires a reachable rippled node (RIPPLED_WS) and a funded MERCHANT_ADDRESS.
MerchantServerOptions options = new()
{
    RippledWsUrl = Environment.GetEnvironmentVariable("RIPPLED_WS") ?? "ws://localhost:6006",
    ListenUrl = Environment.GetEnvironmentVariable("LISTEN_URL") ?? "http://127.0.0.1:5402",
    MerchantAddress = Environment.GetEnvironmentVariable("MERCHANT_ADDRESS") ?? "",
    Network = Environment.GetEnvironmentVariable("NETWORK") ?? "xrpl:1",
    Asset = Environment.GetEnvironmentVariable("ASSET") ?? "XRP",
    Amount = Environment.GetEnvironmentVariable("AMOUNT") ?? "1000000",
    IouIssuer = Environment.GetEnvironmentVariable("IOU_ISSUER"),
    InvoiceId = Environment.GetEnvironmentVariable("INVOICE_ID") ?? "example-invoice-001",
};

if (string.IsNullOrWhiteSpace(options.MerchantAddress))
{
    Console.Error.WriteLine("Set MERCHANT_ADDRESS to a funded XRPL classic address before running.");
    return 1;
}

WebApplication app = await MerchantServer.BuildAsync(options);
await app.StartAsync();

string boundUrl = MerchantServer.ResolveBoundUrl(app);
Console.WriteLine($"[merchant] listening on {boundUrl}");
Console.WriteLine($"[merchant] GET {boundUrl.TrimEnd('/')}/paid requires {options.Amount} {options.Asset} -> {options.MerchantAddress}");
Console.WriteLine("[merchant] press Ctrl+C to stop.");

await app.WaitForShutdownAsync();
return 0;
