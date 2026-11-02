using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class PaymentHandlerSelectionTests
{
    private sealed class FakeSigner : IX402Signer
    {
        public string PayerAddress => "rPayer";
        public Task<string> PrepareAndSignAsync(Payment p, int? maxTimeoutSeconds = null, CancellationToken ct = default)
            => Task.FromResult("SIGNEDBLOB");
    }

    private sealed class TrackingContent : HttpContent
    {
        public bool Disposed;
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return true; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Stub : HttpMessageHandler
    {
        public List<PaymentRequirement> Accepts = new();
        public TrackingContent? ChallengeContent;
        public string? SeenSignature;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.Headers.TryGetValues(X402Headers.PaymentSignature, out IEnumerable<string>? values))
            {
                SeenSignature = values.First();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            HttpResponseMessage challenge = new(HttpStatusCode.PaymentRequired);
            ChallengeContent = new TrackingContent();
            challenge.Content = ChallengeContent;
            PaymentRequiredChallenge body = new();
            foreach (PaymentRequirement accept in Accepts)
                body.Accepts.Add(accept);
            challenge.Headers.Add(X402Headers.PaymentRequired, X402Base64Json.Encode(body));
            return Task.FromResult(challenge);
        }
    }

    private static PaymentRequirement Xrp(string payTo, string amount, string invoice) => new()
    {
        Scheme = "exact", Network = "xrpl:1", Asset = "XRP", PayTo = payTo, Amount = amount, MaxTimeoutSeconds = 60,
        Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{invoice}\"").RootElement },
    };

    private static HttpClient Build(Stub inner, X402ClientOptions options)
        => new(new X402PaymentHandler(new FakeSigner(), options) { InnerHandler = inner });

    [TestMethod]
    public async Task TestUPicksFirstOptionThatPassesPolicy()
    {
        Stub inner = new();
        inner.Accepts.Add(Xrp("rMerchant", "50000000", "inv-expensive"));
        inner.Accepts.Add(Xrp("rMerchant", "1000000", "inv-cheap"));
        HttpClient http = Build(inner, new X402ClientOptions { Network = "xrpl:1", MaxAmountDrops = 10_000_000 });

        HttpResponseMessage r = await http.GetAsync("http://merchant/resource");

        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
        PaymentSignatureEnvelope env = X402Base64Json.Decode<PaymentSignatureEnvelope>(inner.SeenSignature!);
        Assert.AreEqual("inv-cheap", env.Payload.InvoiceId);
    }

    [TestMethod]
    public async Task TestURethrowsFirstPolicyErrorWhenNoOptionPasses()
    {
        Stub inner = new();
        inner.Accepts.Add(Xrp("rEvil", "1000000", "inv-1"));
        inner.Accepts.Add(Xrp("rMerchant", "50000000", "inv-2"));
        X402ClientOptions options = new() { Network = "xrpl:1", MaxAmountDrops = 10_000_000 };
        options.PayToAllowlist.Add("rMerchant");
        HttpClient http = Build(inner, options);

        X402PaymentException ex = await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));

        Assert.AreEqual("payto_not_allowed", ex.Reason);
    }

    [TestMethod]
    public async Task TestUDisposesChallengeResponseWhenNegotiationFails()
    {
        Stub inner = new();
        inner.Accepts.Add(Xrp("rMerchant", "50000000", "inv-1"));
        HttpClient http = Build(inner, new X402ClientOptions { Network = "xrpl:1", MaxAmountDrops = 10 });

        await Assert.ThrowsExactlyAsync<X402PaymentException>(() => http.GetAsync("http://merchant/resource"));

        Assert.IsNotNull(inner.ChallengeContent);
        Assert.IsTrue(inner.ChallengeContent!.Disposed);
    }

    [TestMethod]
    public void TestUDecodeOfInvalidJsonIsAFormatException()
    {
        string notJson = Convert.ToBase64String(Encoding.UTF8.GetBytes("{bad"));

        Assert.ThrowsExactly<FormatException>(() => X402Base64Json.Decode<PaymentRequiredChallenge>(notJson));
    }

    [TestMethod]
    public async Task TestUSkipsOptionThePaymentCannotBeBuiltFor()
    {
        Stub inner = new();
        PaymentRequirement withoutInvoice = Xrp("rMerchant", "1000000", "unused");
        withoutInvoice.Extra.Remove("invoiceId");
        inner.Accepts.Add(withoutInvoice);
        inner.Accepts.Add(Xrp("rMerchant", "1000000", "inv-2"));
        HttpClient http = Build(inner, new X402ClientOptions { Network = "xrpl:1", MaxAmountDrops = 10_000_000 });

        HttpResponseMessage r = await http.GetAsync("http://merchant/resource");

        Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
        PaymentSignatureEnvelope env = X402Base64Json.Decode<PaymentSignatureEnvelope>(inner.SeenSignature!);
        Assert.AreEqual("inv-2", env.Payload.InvoiceId);
    }
}
