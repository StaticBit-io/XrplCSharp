using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

/// <summary>
/// What the <c>RequirePayment</c> filter hands back when settlement does not succeed.
/// </summary>
[TestClass]
public class RequirePaymentTests
{
    private static readonly PaymentRequirement Requirement = new()
    {
        Scheme = "exact", Network = "xrpl:1", Asset = "XRP", PayTo = "rMerchant", Amount = "1000000", MaxTimeoutSeconds = 60
    };

    private sealed class FixedFacilitator : IX402Facilitator
    {
        private readonly PaymentResponseEnvelope _response;
        public FixedFacilitator(PaymentResponseEnvelope response) => _response = response;

        public Task<PaymentResponseEnvelope> VerifyAndSettleAsync(PaymentSignatureEnvelope envelope, CancellationToken cancellationToken = default) =>
            Task.FromResult(_response);
    }

    private static async Task<HttpResponseMessage> PayAsync(PaymentResponseEnvelope settlement)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using WebApplication app = builder.Build();
        app.MapGet("/resource", (HttpContext _) => Results.Text("resource"))
           .RequirePayment(new FixedFacilitator(settlement), _ => Requirement);
        await app.StartAsync();

        HttpClient http = app.GetTestServer().CreateClient();
        HttpRequestMessage request = new(HttpMethod.Get, "/resource");
        request.Headers.Add(X402Headers.PaymentSignature, X402Base64Json.Encode(new PaymentSignatureEnvelope
        {
            Accepted = Requirement,
            Payload = new SignedPayload { SignedTxBlob = "BLOB" }
        }));
        return await http.SendAsync(request);
    }

    /// <summary>
    /// The payer's funds may have moved: the 402 carries the receipt with the hash to reconcile,
    /// beside the challenge.
    /// </summary>
    [TestMethod]
    public async Task AnUnknownSettlementReturnsItsReceiptWithTheChallenge()
    {
        HttpResponseMessage response = await PayAsync(new PaymentResponseEnvelope
        {
            Success = false, ErrorReason = "settlement_unknown", Transaction = "HASH", Network = "xrpl:1", Payer = "rPayer"
        });

        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains(X402Headers.PaymentRequired));
        Assert.IsTrue(response.Headers.TryGetValues(X402Headers.PaymentResponse, out System.Collections.Generic.IEnumerable<string>? receipt));

        PaymentResponseEnvelope envelope = X402Base64Json.Decode<PaymentResponseEnvelope>(string.Join("", receipt!));
        Assert.AreEqual("settlement_unknown", envelope.ErrorReason);
        Assert.AreEqual("HASH", envelope.Transaction);
    }

    [TestMethod]
    public async Task AFailedSettlementReturnsTheChallengeOnly()
    {
        HttpResponseMessage response = await PayAsync(new PaymentResponseEnvelope { Success = false, ErrorReason = "settlement_failed" });

        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains(X402Headers.PaymentResponse));
    }
}
