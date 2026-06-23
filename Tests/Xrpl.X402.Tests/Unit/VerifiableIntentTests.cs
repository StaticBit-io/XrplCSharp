using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class VerifiableIntentTests
{
    private sealed class FakeSigner : IX402Signer
    {
        public string PayerAddress => "rPayer";
        public Task<string> PrepareAndSignAsync(Payment p, int? maxTimeoutSeconds = null, CancellationToken ct = default)
            => Task.FromResult("BLOB");
    }

    private sealed class CapturingInner : HttpMessageHandler
    {
        public string? SeenSignature;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.Headers.TryGetValues(X402Headers.PaymentSignature, out System.Collections.Generic.IEnumerable<string>? v))
            {
                SeenSignature = v.First();
                HttpResponseMessage ok = new(HttpStatusCode.OK) { Content = new StringContent("ok") };
                return Task.FromResult(ok);
            }
            HttpResponseMessage challenge = new(HttpStatusCode.PaymentRequired);
            PaymentRequiredChallenge body = new() { Accepts = { new PaymentRequirement {
                Scheme="exact", Network="xrpl:1", Asset="XRP", PayTo="rM", Amount="1", MaxTimeoutSeconds=60,
                Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"A7F9C76B2EAC41A9B2D500AA76B8FA1800000000000000000000000000000001\"").RootElement } } } };
            challenge.Headers.Add(X402Headers.PaymentRequired, X402Base64Json.Encode(body));
            return Task.FromResult(challenge);
        }
    }

    private sealed class FakeViProvider : IVerifiableIntentProvider
    {
        public Task<object?> CreateExtensionsAsync(PaymentRequirement r, Payment p, CancellationToken ct = default)
            => Task.FromResult<object?>(new { x402Secure = new { verifiableIntentChain = new { l1Credential = "L1" } } });
    }

    [TestMethod]
    public async Task TestUAttachesVerifiableIntentWhenProviderSet()
    {
        CapturingInner inner = new();
        X402ClientOptions opt = new() { Network = "xrpl:1", VerifiableIntentProvider = new FakeViProvider() };
        HttpClient http = new(new X402PaymentHandler(new FakeSigner(), opt) { InnerHandler = inner });

        await http.GetAsync("http://m/resource");

        Assert.IsNotNull(inner.SeenSignature);
        PaymentSignatureEnvelope env = X402Base64Json.Decode<PaymentSignatureEnvelope>(inner.SeenSignature!);
        string json = JsonSerializer.Serialize(env.Extensions);
        StringAssert.Contains(json, "verifiableIntentChain");
        StringAssert.Contains(json, "L1");
    }

    [TestMethod]
    public async Task TestUNoExtensionsKeyWhenNoProvider()
    {
        CapturingInner inner = new();
        X402ClientOptions opt = new() { Network = "xrpl:1" }; // no provider
        HttpClient http = new(new X402PaymentHandler(new FakeSigner(), opt) { InnerHandler = inner });

        await http.GetAsync("http://m/resource");

        // Decode raw JSON of the signature and confirm no "extensions" key was written.
        string raw = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(inner.SeenSignature!));
        Assert.IsFalse(raw.Contains("\"extensions\""), "extensions must be omitted when no provider is configured");
    }
}
