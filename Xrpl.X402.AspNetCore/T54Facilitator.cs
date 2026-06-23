using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.X402.Wire;

namespace Xrpl.X402.AspNetCore;

/// <summary>
/// x402 facilitator that delegates payment verification and on-ledger settlement to the
/// real t54 facilitator service (<c>https://xrpl-facilitator-testnet.t54.ai</c>).
/// <para>
/// The client signs but does NOT submit the transaction; t54 performs verification and submits
/// the signed blob to the XRPL node on its end. The response maps directly to
/// <see cref="PaymentResponseEnvelope"/>.
/// </para>
/// <para>
/// Verification flow: first calls <c>POST {baseUrl}/verify</c> to get a human-readable
/// rejection reason if the payload is invalid, then calls <c>POST {baseUrl}/settle</c> only
/// when verify reports <c>isValid: true</c>.
/// </para>
/// </summary>
public sealed class T54Facilitator : IX402Facilitator
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <summary>
    /// Initializes a new instance of <see cref="T54Facilitator"/>.
    /// </summary>
    /// <param name="http">
    /// <see cref="HttpClient"/> used for the outbound calls to t54.
    /// The caller owns the lifetime of this client.
    /// </param>
    /// <param name="baseUrl">
    /// Base URL of the t54 facilitator service.
    /// Defaults to <c>https://xrpl-facilitator-testnet.t54.ai</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="http"/> is null.</exception>
    public T54Facilitator(HttpClient http, string baseUrl = "https://xrpl-facilitator-testnet.t54.ai")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <inheritdoc />
    /// <remarks>
    /// First calls <c>POST {baseUrl}/verify</c> with body
    /// <c>{ paymentPayload: &lt;envelope&gt;, paymentRequirements: &lt;envelope.Accepted&gt; }</c>.
    /// If <c>isValid == false</c>, returns immediately with the <c>invalidReason</c> from t54.
    /// On success, calls <c>POST {baseUrl}/settle</c> with the same body;
    /// t54 verifies the signed transaction and submits it to the ledger.
    /// </remarks>
    public async Task<PaymentResponseEnvelope> VerifyAndSettleAsync(
        PaymentSignatureEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build the request body shared by /verify and /settle:
            //   { paymentPayload: <PaymentSignatureEnvelope>, paymentRequirements: <PaymentRequirement> }
            object requestBody = new
            {
                paymentPayload = envelope,
                paymentRequirements = envelope.Accepted
            };

            // ── Step 1: /verify ────────────────────────────────────────────────────
            using HttpResponseMessage verifyResponse = await _http.PostAsJsonAsync(
                $"{_baseUrl}/verify",
                requestBody,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!verifyResponse.IsSuccessStatusCode)
            {
                string verifyBody = await verifyResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = $"t54_verify_http_{(int)verifyResponse.StatusCode}: {verifyBody}"
                };
            }

            SpecVerifyResponse? verifyResult = await verifyResponse.Content
                .ReadFromJsonAsync<SpecVerifyResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (verifyResult == null || !verifyResult.IsValid)
            {
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = verifyResult?.InvalidReason ?? "verify_failed"
                };
            }

            // ── Step 2: /settle (only when verify passed) ──────────────────────────
            using HttpResponseMessage settleResponse = await _http.PostAsJsonAsync(
                $"{_baseUrl}/settle",
                requestBody,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!settleResponse.IsSuccessStatusCode)
            {
                string settleBody = await settleResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = $"t54_settle_http_{(int)settleResponse.StatusCode}: {settleBody}"
                };
            }

            PaymentResponseEnvelope? result = await settleResponse.Content
                .ReadFromJsonAsync<PaymentResponseEnvelope>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return result ?? new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "t54_empty_response"
            };
        }
        catch (Exception ex)
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = $"t54_error: {ex.Message}"
            };
        }
    }

    /// <summary>t54 /verify response schema.</summary>
    private sealed class SpecVerifyResponse
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("invalidReason")]
        public string? InvalidReason { get; set; }

        [JsonPropertyName("payer")]
        public string? Payer { get; set; }
    }
}
