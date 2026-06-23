using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xrpl.X402.Wire;

namespace Xrpl.X402.AspNetCore;

/// <summary>Extensions to protect ASP.NET Core minimal-API endpoints with x402 payment.</summary>
public static class X402PaymentExtensions
{
    /// <summary>
    /// Require an x402 payment for this endpoint. Without a valid PAYMENT-SIGNATURE the endpoint
    /// returns 402 + PAYMENT-REQUIRED challenge; with a valid, settled signature it sets
    /// PAYMENT-RESPONSE and executes the handler.
    /// </summary>
    /// <typeparam name="TBuilder">Any <see cref="IEndpointConventionBuilder"/>.</typeparam>
    /// <param name="builder">The endpoint convention builder to protect.</param>
    /// <param name="facilitator">Facilitator that verifies and settles the payment on-ledger.</param>
    /// <param name="requirementFactory">
    /// Factory that produces the <see cref="PaymentRequirement"/> for a given request,
    /// allowing per-request price/payTo/invoiceId customisation.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder RequirePayment<TBuilder>(
        this TBuilder builder,
        IX402Facilitator facilitator,
        Func<HttpContext, PaymentRequirement> requirementFactory)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            HttpContext http = ctx.HttpContext;
            PaymentRequirement requirement = requirementFactory(http);

            // No payment header → issue 402 challenge
            if (!http.Request.Headers.TryGetValue(X402Headers.PaymentSignature, out StringValues sig)
                || string.IsNullOrWhiteSpace(sig))
            {
                http.Response.Headers[X402Headers.PaymentRequired] =
                    X402Base64Json.Encode(new PaymentRequiredChallenge { Accepts = { requirement } });
                return Results.StatusCode(StatusCodes.Status402PaymentRequired);
            }

            PaymentResponseEnvelope settle;
            try
            {
                PaymentSignatureEnvelope env = X402Base64Json.Decode<PaymentSignatureEnvelope>(sig!);
                settle = await facilitator.VerifyAndSettleAsync(env, http.RequestAborted);
            }
            catch
            {
                return Results.StatusCode(StatusCodes.Status402PaymentRequired);
            }

            if (!settle.Success)
                return Results.StatusCode(StatusCodes.Status402PaymentRequired);

            http.Response.Headers[X402Headers.PaymentResponse] = X402Base64Json.Encode(settle);
            return await next(ctx);
        });
        return builder;
    }
}
