using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Xrpl.Client;
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Examples.MerchantServer;

/// <summary>
/// Configuration for the example x402 merchant server.
/// </summary>
public sealed class MerchantServerOptions
{
    /// <summary>WebSocket URL of the rippled node used to settle payments on-ledger.</summary>
    public string RippledWsUrl { get; set; } = "ws://localhost:6006";

    /// <summary>
    /// URL Kestrel binds to. Use a <c>:0</c> port to let the OS pick a free one. Leave empty to
    /// defer to the launch profile / <c>ASPNETCORE_URLS</c> (so Visual Studio F5 works cleanly).
    /// </summary>
    public string ListenUrl { get; set; } = "";

    /// <summary>Classic XRPL address that receives the payment (the merchant). Required.</summary>
    public string MerchantAddress { get; set; } = "";

    /// <summary>CAIP-2 network advertised in the 402 challenge.</summary>
    public string Network { get; set; } = "xrpl:1";

    /// <summary>Asset ticker: <c>"XRP"</c> or a 40-hex currency code (e.g. RLUSD).</summary>
    public string Asset { get; set; } = "XRP";

    /// <summary>Amount to charge: drops for XRP, decimal value string for IOUs.</summary>
    public string Amount { get; set; } = "1000000";

    /// <summary>Issuer address for IOU assets (ignored for XRP).</summary>
    public string? IouIssuer { get; set; }

    /// <summary>Maximum seconds the signed payment stays valid (maps to LastLedgerSequence).</summary>
    public int MaxTimeoutSeconds { get; set; } = 60;

    /// <summary>Invoice id bound to the payment (echoed in <c>extra.invoiceId</c>).</summary>
    public string InvoiceId { get; set; } = "example-invoice-001";

    /// <summary>Body returned to the client once the payment settles.</summary>
    public string ResourceBody { get; set; } = "premium content";
}

/// <summary>
/// Example x402 merchant: a minimal-API endpoint protected by <c>RequirePayment</c> that settles
/// the client's signed payment against a local rippled via <see cref="LedgerSettlingFacilitator"/>.
/// </summary>
public static class MerchantServer
{
    /// <summary>
    /// Connects to rippled and builds (but does not start) a <see cref="WebApplication"/> exposing
    /// <c>GET /paid</c> behind an x402 payment requirement. The caller owns the app lifecycle
    /// (<c>StartAsync</c>/<c>StopAsync</c>), which lets tests host it on a real socket.
    /// </summary>
    public static async Task<WebApplication> BuildAsync(MerchantServerOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.MerchantAddress))
            throw new ArgumentException("MerchantAddress is required.", nameof(options));
        if (!string.Equals(options.Asset, "XRP", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.IouIssuer))
            throw new ArgumentException("IouIssuer is required when Asset is not XRP.", nameof(options));

        XrplClient client = new(options.RippledWsUrl, options: new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 3,
            ReconnectBaseDelay = TimeSpan.FromSeconds(5),
            ReconnectMaxDelay = TimeSpan.FromSeconds(6),
            RequestPolicy = RequestFailurePolicy.ImmediateFail,
            StopAfterMaxAttempts = true,
            UseCustomPing = false,
        });
        await client.Connect();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        if (!string.IsNullOrWhiteSpace(options.ListenUrl))
            builder.WebHost.UseUrls(options.ListenUrl);
        WebApplication app = builder.Build();

        IX402Facilitator facilitator = new LedgerSettlingFacilitator(client);

        // Friendly landing page so a plain browser GET / shows guidance instead of a bare 404.
        app.MapGet("/", () => Results.Text(
            "x402 merchant example\n" +
            "\n" +
            "Paid resource : GET /paid\n" +
            $"Price         : {options.Amount} {options.Asset} -> {options.MerchantAddress}\n" +
            $"Network       : {options.Network}\n" +
            "\n" +
            "A plain browser request to /paid returns 402 (Payment Required) with an x402\n" +
            "PAYMENT-REQUIRED challenge. To receive the content, pay the challenge with the\n" +
            "X402.PayingClient example (it signs a Payment and retries automatically).\n",
            "text/plain"));

        // Without a valid PAYMENT-SIGNATURE this returns 402 + PAYMENT-REQUIRED; with one, the
        // facilitator settles the payment on-ledger, sets PAYMENT-RESPONSE, and the handler runs.
        app.MapGet("/paid", () => Results.Text(options.ResourceBody))
           .RequirePayment(facilitator, _ => BuildRequirement(options));

        return app;
    }

    /// <summary>Builds the <see cref="PaymentRequirement"/> this server charges, from its options.</summary>
    public static PaymentRequirement BuildRequirement(MerchantServerOptions options)
    {
        Dictionary<string, JsonElement> extra = new()
        {
            ["invoiceId"] = JsonSerializer.SerializeToElement(options.InvoiceId)
        };

        if (!string.IsNullOrWhiteSpace(options.IouIssuer))
            extra["issuer"] = JsonSerializer.SerializeToElement(options.IouIssuer);

        return new PaymentRequirement
        {
            Scheme = "exact",
            Network = options.Network,
            Asset = options.Asset,
            PayTo = options.MerchantAddress,
            Amount = options.Amount,
            MaxTimeoutSeconds = options.MaxTimeoutSeconds,
            Extra = extra
        };
    }

    /// <summary>
    /// Returns the actual URL the server is listening on. Call only after <c>StartAsync</c>;
    /// resolves the real port when the app was bound to a <c>:0</c> port.
    /// </summary>
    public static string ResolveBoundUrl(WebApplication app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));

        IServerAddressesFeature? addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();

        string? url = addresses?.Addresses.FirstOrDefault();
        return url ?? throw new InvalidOperationException(
            "The server has no bound address yet; call ResolveBoundUrl after StartAsync().");
    }
}
