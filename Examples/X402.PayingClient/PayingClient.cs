using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Examples.PayingClient;

/// <summary>
/// Configuration for the example paying client.
/// </summary>
public sealed class PayingClientOptions
{
    /// <summary>Absolute URL of the payment-protected resource (e.g. the merchant's <c>/paid</c>).</summary>
    public string ResourceUrl { get; set; } = "http://127.0.0.1:5402/paid";

    /// <summary>Seed of the XRPL wallet that funds and signs the payment.</summary>
    public string PayerSeed { get; set; } = "";

    /// <summary>WebSocket URL of the rippled node used to autofill/sign the payment.</summary>
    public string RippledWsUrl { get; set; } = "ws://localhost:6006";

    /// <summary>CAIP-2 network the client is willing to pay on.</summary>
    public string Network { get; set; } = "xrpl:1";

    /// <summary>Hard cap for XRP payments, in drops.</summary>
    public ulong MaxAmountDrops { get; set; } = 10_000_000;

    /// <summary>Optional per-issuer value caps (required to pay any IOU/RLUSD).</summary>
    public Dictionary<string, decimal>? IouValueCaps { get; set; }
}

/// <summary>Outcome of fetching a payment-protected resource.</summary>
public sealed record PaidResult(string Body, bool Settled, string? TxHash, string? Payer);

/// <summary>
/// Example x402 client: wraps an <see cref="HttpClient"/> with <see cref="X402PaymentHandler"/> so a
/// 402 response is paid (signed locally, settled by the merchant's facilitator) and retried
/// transparently. The resource body is returned alongside the settlement receipt.
/// </summary>
public static class PayingClient
{
    /// <summary>
    /// Connects to rippled, builds a payment-aware <see cref="HttpClient"/>, and fetches the
    /// configured resource — paying its 402 challenge automatically.
    /// </summary>
    public static async Task<PaidResult> FetchAsync(PayingClientOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.PayerSeed))
            throw new ArgumentException("PayerSeed is required.", nameof(options));

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

        XrplWallet payer = XrplWallet.FromSeed(options.PayerSeed);
        IX402Signer signer = new XrplWalletX402Signer(client, payer);

        X402ClientOptions clientOptions = new()
        {
            Network = options.Network,
            MaxAmountDrops = options.MaxAmountDrops,
        };

        if (options.IouValueCaps is not null)
        {
            foreach (KeyValuePair<string, decimal> cap in options.IouValueCaps)
                clientOptions.IouValueCaps[cap.Key] = cap.Value;
        }

        using X402PaymentHandler handler = new(signer, clientOptions) { InnerHandler = new HttpClientHandler() };
        using HttpClient http = new(handler);

        using HttpResponseMessage response = await http.GetAsync(options.ResourceUrl);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();

        if (response.Headers.TryGetValues(X402Headers.PaymentResponse, out IEnumerable<string>? receiptHeaders))
        {
            PaymentResponseEnvelope receipt = X402Base64Json.Decode<PaymentResponseEnvelope>(receiptHeaders.First());
            return new PaidResult(body, receipt.Success, receipt.Transaction, receipt.Payer);
        }

        return new PaidResult(body, false, null, null);
    }
}
