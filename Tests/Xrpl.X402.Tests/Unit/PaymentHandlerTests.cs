using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class PaymentHandlerTests
{
    private sealed class FakeSigner : IX402Signer
    {
        public string PayerAddress => "rPayer";
        public int Calls;
        public Task<string> PrepareAndSignAsync(Payment p, CancellationToken ct = default)
        { Calls++; return Task.FromResult("SIGNEDBLOB"); }
    }

    private sealed class StubInner : HttpMessageHandler
    {
        public bool AlwaysPaymentRequired;
        public string? SeenSignature;
        public PaymentRequirement? Requirement;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            bool hasSig = req.Headers.TryGetValues(X402Headers.PaymentSignature, out System.Collections.Generic.IEnumerable<string>? v);
            if (hasSig) SeenSignature = v!.First();

            if (!hasSig || AlwaysPaymentRequired)
            {
                HttpResponseMessage challenge = new(HttpStatusCode.PaymentRequired);
                PaymentRequirement requirement = Requirement ?? new PaymentRequirement {
                    Scheme="exact", Network="xrpl:1", Asset="XRP", PayTo="rMerchant",
                    Amount="1000000", MaxTimeoutSeconds=60,
                    Extra = new() { ["invoiceId"]=System.Text.Json.JsonDocument.Parse("\"inv\"").RootElement }
                };
                PaymentRequiredChallenge body = new() { Accepts = { requirement } };
                challenge.Headers.Add(X402Headers.PaymentRequired, X402Base64Json.Encode(body));
                return Task.FromResult(challenge);
            }

            HttpResponseMessage ok = new(HttpStatusCode.OK) { Content = new StringContent("resource") };
            PaymentResponseEnvelope resp = new() { Success=true, Transaction="HASH", Payer="rPayer" };
            ok.Headers.Add(X402Headers.PaymentResponse, X402Base64Json.Encode(resp));
            return Task.FromResult(ok);
        }
    }

    private static HttpClient Build(StubInner inner, FakeSigner signer, X402ClientOptions opt)
    {
        X402PaymentHandler handler = new(signer, opt) { InnerHandler = inner };
        return new HttpClient(handler);
    }

    [TestMethod]
    public async Task TestUPaysOnceAndReturnsResource()
    {
        StubInner inner = new();
        FakeSigner signer = new();
        HttpClient http = Build(inner, signer, new X402ClientOptions { Network="xrpl:1", MaxAmountDrops=10_000_000 });

        HttpResponseMessage r = await http.GetAsync("http://merchant/resource");

        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
        Assert.AreEqual("resource", await r.Content.ReadAsStringAsync());
        Assert.AreEqual(1, signer.Calls);
        Assert.IsNotNull(inner.SeenSignature);
    }

    [TestMethod]
    public async Task TestURefusesWhenAmountOverCap()
    {
        StubInner inner = new();
        FakeSigner signer = new();
        HttpClient http = Build(inner, signer, new X402ClientOptions { Network="xrpl:1", MaxAmountDrops=500_000 });

        await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));
        Assert.AreEqual(0, signer.Calls);
    }

    [TestMethod]
    public async Task TestUThrowsOnRepeated402()
    {
        StubInner inner = new() { AlwaysPaymentRequired = true };
        FakeSigner signer = new();
        HttpClient http = Build(inner, signer, new X402ClientOptions { Network="xrpl:1", MaxAmountDrops=10_000_000 });

        await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));
    }

    [TestMethod]
    public async Task TestURefusesUnparseableIouAmountWhenCapped()
    {
        StubInner inner = new()
        {
            Requirement = new PaymentRequirement
            {
                Scheme = "exact", Network = "xrpl:1",
                Asset = "524C555344000000000000000000000000000000",
                PayTo = "rMerchant", Amount = "not-a-number", MaxTimeoutSeconds = 60,
                Extra = new()
                {
                    ["invoiceId"] = System.Text.Json.JsonDocument.Parse("\"inv\"").RootElement,
                    ["issuer"] = System.Text.Json.JsonDocument.Parse("\"rIssuer\"").RootElement
                }
            }
        };
        FakeSigner signer = new();
        X402ClientOptions opt = new() { Network = "xrpl:1" };
        opt.IouValueCaps["rIssuer"] = 10m;
        HttpClient http = Build(inner, signer, opt);

        await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));
        Assert.AreEqual(0, signer.Calls);
    }

    [TestMethod]
    public async Task TestURefusesUncappedIouIssuer()
    {
        StubInner inner = new()
        {
            Requirement = new PaymentRequirement
            {
                Scheme = "exact", Network = "xrpl:1",
                Asset = "524C555344000000000000000000000000000000",
                PayTo = "rMerchant", Amount = "2.5", MaxTimeoutSeconds = 60,
                Extra = new()
                {
                    ["invoiceId"] = System.Text.Json.JsonDocument.Parse("\"inv\"").RootElement,
                    ["issuer"] = System.Text.Json.JsonDocument.Parse("\"rUnknownIssuer\"").RootElement
                }
            }
        };
        FakeSigner signer = new();
        HttpClient http = Build(inner, signer, new X402ClientOptions { Network = "xrpl:1" }); // empty IouValueCaps

        await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));
        Assert.AreEqual(0, signer.Calls);
    }

    [TestMethod]
    public async Task TestURefusesIssuerNotInAllowlist()
    {
        StubInner inner = new()
        {
            Requirement = new PaymentRequirement
            {
                Scheme = "exact", Network = "xrpl:1",
                Asset = "524C555344000000000000000000000000000000",
                PayTo = "rMerchant", Amount = "2.5", MaxTimeoutSeconds = 60,
                Extra = new()
                {
                    ["invoiceId"] = System.Text.Json.JsonDocument.Parse("\"inv\"").RootElement,
                    ["issuer"] = System.Text.Json.JsonDocument.Parse("\"rIssuer\"").RootElement
                }
            }
        };
        FakeSigner signer = new();
        X402ClientOptions opt = new() { Network = "xrpl:1" };
        opt.IouValueCaps["rIssuer"] = 10m;          // capped...
        opt.PayToAllowlist.Add("rMerchant");        // ...but issuer not allowlisted
        HttpClient http = Build(inner, signer, opt);

        await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));
        Assert.AreEqual(0, signer.Calls);
    }
}
