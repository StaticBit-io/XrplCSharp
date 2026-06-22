using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.Models.Transactions;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

/// <summary>
/// A <see cref="DelegatingHandler"/> that automatically pays HTTP 402 challenges using the XRPL x402 protocol.
/// On a 402 response the handler selects the matching <see cref="PaymentRequirement"/>, enforces configured
/// amount caps and allowlists, signs the payment via <see cref="IX402Signer"/>, and retries the original request
/// with the <c>PAYMENT-SIGNATURE</c> header attached.
/// </summary>
public sealed class X402PaymentHandler : DelegatingHandler
{
    private readonly IX402Signer _signer;
    private readonly X402ClientOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="X402PaymentHandler"/>.
    /// </summary>
    /// <param name="signer">Signer that autofills and locally signs XRPL payment transactions.</param>
    /// <param name="options">Client options controlling network selection, amount caps, and allowlists.</param>
    public X402PaymentHandler(IX402Signer signer, X402ClientOptions options)
    {
        _signer = signer;
        _options = options;
    }

    /// <summary>
    /// Sends the request; if a 402 Payment Required response is received, negotiates and pays the challenge,
    /// then retries the request with the payment proof attached.
    /// </summary>
    /// <param name="request">The HTTP request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final <see cref="HttpResponseMessage"/> after any payment negotiation.</returns>
    /// <exception cref="X402PaymentException">
    /// Thrown when the challenge is malformed, no acceptable requirement is found, a policy cap is exceeded,
    /// or the server returns 402 again after payment (anti double-pay guard).
    /// </exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync();

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.PaymentRequired)
            return response;

        PaymentRequirement requirement = SelectRequirement(response);
        EnforcePolicy(requirement);

        Payment payment = X402PaymentBuilder.Build(requirement, _signer.PayerAddress, _options);
        string signedBlob = await _signer.PrepareAndSignAsync(payment, requirement.MaxTimeoutSeconds, cancellationToken);

        PaymentSignatureEnvelope envelope = new()
        {
            X402Version = _options.X402Version,
            Accepted = requirement,
            Payload = new SignedPayload { SignedTxBlob = signedBlob },
        };

        if (_options.VerifiableIntentProvider is not null)
            envelope.Extensions = await _options.VerifiableIntentProvider.CreateExtensionsAsync(requirement, payment, cancellationToken);

        response.Dispose();
        using HttpRequestMessage retry = await CloneAsync(request);
        retry.Headers.Remove(X402Headers.PaymentSignature);
        retry.Headers.Add(X402Headers.PaymentSignature, X402Base64Json.Encode(envelope));

        HttpResponseMessage paid = await base.SendAsync(retry, cancellationToken);
        if (paid.StatusCode == HttpStatusCode.PaymentRequired)
            throw new X402PaymentException("payment_rejected",
                "Server still returned 402 after payment (anti double-pay guard).");
        return paid;
    }

    private PaymentRequirement SelectRequirement(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(X402Headers.PaymentRequired, out System.Collections.Generic.IEnumerable<string>? values))
            throw new X402PaymentException("invalid_challenge", "402 without PAYMENT-REQUIRED header.");

        string? headerValue = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue))
            throw new X402PaymentException("invalid_challenge", "PAYMENT-REQUIRED header had no value.");
        PaymentRequiredChallenge challenge = X402Base64Json.Decode<PaymentRequiredChallenge>(headerValue);
        PaymentRequirement? match = challenge.Accepts.FirstOrDefault(r =>
            string.Equals(r.Scheme, "exact", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Network, _options.Network, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new X402PaymentException("no_acceptable_requirement",
            $"No 'exact' requirement on network {_options.Network}.");
    }

    private void EnforcePolicy(PaymentRequirement req)
    {
        bool restrict = _options.PayToAllowlist.Count > 0;
        if (restrict && !_options.PayToAllowlist.Contains(req.PayTo))
            throw new X402PaymentException("payto_not_allowed", $"payTo {req.PayTo} not in allowlist.");

        if (X402AmountMapper.IsXrp(req))
        {
            if (!ulong.TryParse(req.Amount, out ulong drops) || drops > _options.MaxAmountDrops)
                throw new X402PaymentException("amount_over_cap",
                    $"XRP amount {req.Amount} drops exceeds cap {_options.MaxAmountDrops}.");
            return;
        }

        // IOU/RLUSD: fail closed. An explicit per-issuer cap is REQUIRED to pay.
        string issuer = req.Extra.TryGetValue("issuer", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()! : "";
        if (restrict && !_options.PayToAllowlist.Contains(issuer))
            throw new X402PaymentException("issuer_not_allowed", $"issuer {issuer} not in allowlist.");
        if (!_options.IouValueCaps.TryGetValue(issuer, out decimal cap))
            throw new X402PaymentException("amount_over_cap",
                $"No IOU cap configured for issuer {issuer}; refusing.");
        if (!decimal.TryParse(req.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal val))
            throw new X402PaymentException("amount_over_cap", $"IOU amount '{req.Amount}' is not a valid decimal.");
        if (val > cap)
            throw new X402PaymentException("amount_over_cap", $"IOU amount {val} exceeds cap {cap} for issuer {issuer}.");
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage req)
    {
        HttpRequestMessage clone = new(req.Method, req.RequestUri) { Version = req.Version };
        foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>> h in req.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (req.Content is not null)
        {
            byte[] body = await req.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(body);
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>> h in req.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }
}
