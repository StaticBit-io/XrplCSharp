using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Integration;

/// <summary>
/// Minimal ASP.NET merchant running on a TestServer. Exposes GET /resource:
/// - No PAYMENT-SIGNATURE header → 402 with PAYMENT-REQUIRED challenge.
/// - Valid PAYMENT-SIGNATURE header → facilitator settles; 200 with PAYMENT-RESPONSE, or 402 on failure.
/// </summary>
public sealed class TestMerchant
{
    private readonly TestFacilitator _facilitator;
    private readonly PaymentRequirement _requirement;

    public TestMerchant(TestFacilitator facilitator, PaymentRequirement requirement)
    {
        _facilitator = facilitator;
        _requirement = requirement;
    }

    public WebApplication Build()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        WebApplication app = builder.Build();

        app.MapGet("/resource", async (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.TryGetValue(X402Headers.PaymentSignature, out Microsoft.Extensions.Primitives.StringValues sigHeader)
                || string.IsNullOrEmpty(sigHeader))
            {
                PaymentRequiredChallenge challenge = new()
                {
                    Accepts = { _requirement }
                };
                ctx.Response.Headers[X402Headers.PaymentRequired] = X402Base64Json.Encode(challenge);
                ctx.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                return;
            }

            PaymentSignatureEnvelope env = X402Base64Json.Decode<PaymentSignatureEnvelope>(sigHeader.ToString());
            PaymentResponseEnvelope settle = await _facilitator.VerifyAndSettleAsync(env, ctx.RequestAborted);

            if (!settle.Success)
            {
                ctx.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                return;
            }

            ctx.Response.Headers[X402Headers.PaymentResponse] = X402Base64Json.Encode(settle);
            await ctx.Response.WriteAsync("resource");
        });

        return app;
    }
}
