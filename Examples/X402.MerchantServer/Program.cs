using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Xrpl.X402.Examples.MerchantServer;

// Configuration comes from appsettings.json (the "X402" section); any value can be overridden by
// an environment variable or a --Key=value command-line argument. See appsettings.json for the
// full list of knobs.
WebApplicationBuilder configBuilder = WebApplication.CreateBuilder(args);
MerchantServerOptions options =
    configBuilder.Configuration.GetSection("X402").Get<MerchantServerOptions>() ?? new MerchantServerOptions();

// Friendly flat environment-variable overrides (handy for shell/CI; appsettings is the primary source).
options.RippledWsUrl = Environment.GetEnvironmentVariable("RIPPLED_WS") ?? options.RippledWsUrl;
options.ListenUrl = Environment.GetEnvironmentVariable("LISTEN_URL") ?? options.ListenUrl;
options.MerchantAddress = Environment.GetEnvironmentVariable("MERCHANT_ADDRESS") ?? options.MerchantAddress;
options.Asset = Environment.GetEnvironmentVariable("ASSET") ?? options.Asset;
options.Amount = Environment.GetEnvironmentVariable("AMOUNT") ?? options.Amount;
options.IouIssuer = Environment.GetEnvironmentVariable("IOU_ISSUER") ?? options.IouIssuer;
options.InvoiceId = Environment.GetEnvironmentVariable("INVOICE_ID") ?? options.InvoiceId;

if (string.IsNullOrWhiteSpace(options.MerchantAddress))
    throw new InvalidOperationException(
        "X402:MerchantAddress is not configured. Set it in appsettings.json (the X402 section) " +
        "or via the MERCHANT_ADDRESS environment variable, then run again.");

WebApplication app = await MerchantServer.BuildAsync(options);
await app.StartAsync();

string boundUrl = MerchantServer.ResolveBoundUrl(app);
Console.WriteLine($"[merchant] listening on {boundUrl}");
Console.WriteLine($"[merchant] GET {boundUrl.TrimEnd('/')}/paid requires {options.Amount} {options.Asset} -> {options.MerchantAddress}");
Console.WriteLine("[merchant] press Ctrl+C to stop.");

await app.WaitForShutdownAsync();
