using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

// Covers BaseTransactionResponse.UnknownFields. Measured on live mainnet responses: rippled sends
// "ctid" (compact transaction ID) on tx/account_tx responses, and this SDK did not model it —
// before this attribute it was silently dropped on deserialize.
[TestClass]
public class TestUTransactionResponseExtensionData
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    private const string PaymentJsonWithCtid = @"{
        ""TransactionType"": ""Payment"",
        ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
        ""Destination"": ""rDestAccount1111111111111111111"",
        ""Amount"": ""1000000"",
        ""Fee"": ""10"",
        ""Sequence"": 5,
        ""hash"": ""ABCDEF0123456789"",
        ""ctid"": ""C000001200000000""
    }";

    [TestMethod]
    public void Deserialize_PaymentResponse_Direct_CapturesUnknownField()
    {
        PaymentResponse result = JsonSerializer.Deserialize<PaymentResponse>(PaymentJsonWithCtid, Options);

        Assert.IsNotNull(result.UnknownFields);
        Assert.IsTrue(result.UnknownFields.ContainsKey("ctid"));
        Assert.AreEqual("C000001200000000", result.UnknownFields["ctid"].GetString());
    }

    [TestMethod]
    public void Deserialize_ITransactionResponse_ThroughConverter_CapturesUnknownField()
    {
        // Goes through TransactionResponseConverter.Read -> Create(transactionType) -> the ordinary
        // reflection-based deserializer for the concrete PaymentResponse type. The converter is
        // stripped from the inner options only to avoid re-entering itself for the same interface;
        // it never intercepts field-level reads on the concrete response type.
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(PaymentJsonWithCtid, Options);

        Assert.IsInstanceOfType(result, typeof(PaymentResponse));
        PaymentResponse payment = (PaymentResponse)result;
        Assert.IsNotNull(payment.UnknownFields);
        Assert.IsTrue(payment.UnknownFields.ContainsKey("ctid"));
    }

    [TestMethod]
    public void Deserialize_PaymentResponse_WithOnlyKnownFields_LeavesUnknownFieldsEmpty()
    {
        string json = @"{
            ""TransactionType"": ""Payment"",
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""Destination"": ""rDestAccount1111111111111111111"",
            ""Amount"": ""1000000"",
            ""Fee"": ""10"",
            ""Sequence"": 5,
            ""hash"": ""ABCDEF0123456789""
        }";

        PaymentResponse result = JsonSerializer.Deserialize<PaymentResponse>(json, Options);

        Assert.IsTrue(result.UnknownFields == null || result.UnknownFields.Count == 0);
    }

    [TestMethod]
    public void Serialize_ITransactionResponse_RoundTripsUnknownField()
    {
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(PaymentJsonWithCtid, Options);

        string output = JsonSerializer.Serialize(result, Options);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.IsTrue(doc.RootElement.TryGetProperty("ctid", out JsonElement value));
        Assert.AreEqual("C000001200000000", value.GetString());
    }
}
