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
    /// <see cref="HttpClient"/> used for the outbound call to t54.
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
    /// Calls <c>POST {baseUrl}/settle</c> with body
    /// <c>{ paymentPayload: &lt;envelope&gt;, paymentRequirements: &lt;envelope.Accepted&gt; }</c>.
    /// t54 verifies the signed transaction and submits it to the ledger; the response carries the
    /// validated transaction hash on success.
    /// </remarks>
    public async Task<PaymentResponseEnvelope> VerifyAndSettleAsync(
        PaymentSignatureEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build the request body as t54's SpecSettleRequest:
            //   { paymentPayload: <PaymentSignatureEnvelope>, paymentRequirements: <PaymentRequirement> }
            object requestBody = new
            {
                paymentPayload = envelope,
                paymentRequirements = envelope.Accepted
            };

            using HttpResponseMessage httpResponse = await _http.PostAsJsonAsync(
                $"{_baseUrl}/settle",
                requestBody,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = $"t54_http_{(int)httpResponse.StatusCode}"
                };
            }

            PaymentResponseEnvelope? result = await httpResponse.Content
                .ReadFromJsonAsync<PaymentResponseEnvelope>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return result ?? new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "t54_empty_response"
            };
        }
        catch (Exception)
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "t54_error"
            };
        }
    }
}
