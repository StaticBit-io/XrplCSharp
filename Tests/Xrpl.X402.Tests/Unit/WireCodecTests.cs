using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class WireCodecTests
{
    [TestMethod]
    public void TestUEncodeDecodeRoundTrip()
    {
        PaymentSignatureEnvelope env = new()
        {
            X402Version = 2,
            Accepted = new PaymentRequirement
            {
                Scheme = "exact", Network = "xrpl:1", Asset = "XRP",
                PayTo = "rMerchant", Amount = "1000000", MaxTimeoutSeconds = 60,
                Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"inv-1\"").RootElement, ["sourceTag"] = JsonDocument.Parse("804681468").RootElement }
            },
            Payload = new SignedPayload { SignedTxBlob = "DEADBEEF" }
        };

        string header = X402Base64Json.Encode(env);
        PaymentSignatureEnvelope back = X402Base64Json.Decode<PaymentSignatureEnvelope>(header);

        Assert.AreEqual(2, back.X402Version);
        Assert.AreEqual("exact", back.Accepted.Scheme);
        Assert.AreEqual("DEADBEEF", back.Payload.SignedTxBlob);
        Assert.AreEqual("inv-1", back.Accepted.Extra["invoiceId"].GetString());
    }
}
