using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

// Covers BaseTransactionResponse.UnknownFields. Measured on live mainnet responses: rippled sends
// fields this SDK did not model at the time — before this attribute they were silently dropped on
// deserialize. "ctid" (compact transaction ID) was the original trigger for this coverage; it has
// since become a modeled property (BaseTransactionResponse.Ctid — see
// TestUBaseTransactionResponseFields), so the fixture below now exercises an unrelated field that
// stays genuinely unknown, so this file keeps covering what it was written to cover.
[TestClass]
public class TestUTransactionResponseExtensionData
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    private const string PaymentJsonWithUnknownField = @"{
        ""TransactionType"": ""Payment"",
        ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
        ""Destination"": ""rDestAccount1111111111111111111"",
        ""Amount"": ""1000000"",
        ""Fee"": ""10"",
        ""Sequence"": 5,
        ""hash"": ""ABCDEF0123456789"",
        ""a_field_no_model_knows"": ""C000001200000000""
    }";

    [TestMethod]
    public void Deserialize_PaymentResponse_Direct_CapturesUnknownField()
    {
        PaymentResponse result = JsonSerializer.Deserialize<PaymentResponse>(PaymentJsonWithUnknownField, Options);

        Assert.IsNotNull(result.UnknownFields);
        Assert.IsTrue(result.UnknownFields.ContainsKey("a_field_no_model_knows"));
        Assert.AreEqual("C000001200000000", result.UnknownFields["a_field_no_model_knows"].GetString());
    }

    [TestMethod]
    public void Deserialize_ITransactionResponse_ThroughConverter_CapturesUnknownField()
    {
        // Goes through TransactionResponseConverter.Read -> Create(transactionType) -> the ordinary
        // reflection-based deserializer for the concrete PaymentResponse type. The converter is
        // stripped from the inner options only to avoid re-entering itself for the same interface;
        // it never intercepts field-level reads on the concrete response type.
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(PaymentJsonWithUnknownField, Options);

        Assert.IsInstanceOfType(result, typeof(PaymentResponse));
        PaymentResponse payment = (PaymentResponse)result;
        Assert.IsNotNull(payment.UnknownFields);
        Assert.IsTrue(payment.UnknownFields.ContainsKey("a_field_no_model_knows"));
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
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(PaymentJsonWithUnknownField, Options);

        string output = JsonSerializer.Serialize(result, Options);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.IsTrue(doc.RootElement.TryGetProperty("a_field_no_model_knows", out JsonElement value));
        Assert.AreEqual("C000001200000000", value.GetString());
    }
}
