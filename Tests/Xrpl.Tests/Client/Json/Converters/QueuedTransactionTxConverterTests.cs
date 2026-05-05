using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;

using XrplTests;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class QueuedTransactionTxConverterTests
{
    [TestMethod]
    public void Read_StringTx_ReturnsHash()
    {
        string json = @"{
            ""account"": ""rN7n7otQDd6FczFgLdlqtyMVrn3HMdfsCk"",
            ""tx"": ""E7A4A2D3E3B3D2D9E0C0A1B3C4D5E6F7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2"",
            ""retries_remaining"": 5,
            ""preflight_result"": ""tesSUCCESS""
        }";
        QueuedTransaction result = JsonSerializer.Deserialize<QueuedTransaction>(json, XrplJsonOptions.Default);
        Assert.IsInstanceOfType(result.Transaction, typeof(string));
        Assert.AreEqual(
            "E7A4A2D3E3B3D2D9E0C0A1B3C4D5E6F7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2",
            (string)result.Transaction);
    }

    [TestMethod]
    public void Read_ObjectTx_ReturnsJsonElement()
    {
        string json = @"{
            ""account"": ""rN7n7otQDd6FczFgLdlqtyMVrn3HMdfsCk"",
            ""tx"": {
                ""hash"": ""ABC123"",
                ""TransactionType"": ""Payment"",
                ""Account"": ""rTest""
            },
            ""retries_remaining"": 5,
            ""preflight_result"": ""tesSUCCESS""
        }";
        QueuedTransaction result = JsonSerializer.Deserialize<QueuedTransaction>(json, XrplJsonOptions.Default);
        Assert.IsInstanceOfType(result.Transaction, typeof(JsonElement));
        JsonElement tx = (JsonElement)result.Transaction;
        Assert.AreEqual("ABC123", tx.GetProperty("hash").GetString());
    }

    [TestMethod]
    public void RoundTrip_Hash_Preserved()
    {
        QueuedTransaction original = new QueuedTransaction
        {
            Account = "rN7n7otQDd6FczFgLdlqtyMVrn3HMdfsCk",
            Transaction = "DEADBEEFDEADBEEFDEADBEEFDEADBEEFDEADBEEF",
            RetriesRemaining = 3,
            PreflightResult = "tesSUCCESS",
        };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        QueuedTransaction deserialized = JsonSerializer.Deserialize<QueuedTransaction>(json, XrplJsonOptions.Default);
        Assert.AreEqual(original.Transaction, deserialized.Transaction);
    }

    [TestMethod]
    public void RoundTrip_Object_Preserved()
    {
        string innerJson = "{\"hash\":\"H1\",\"TransactionType\":\"Payment\"}";
        JsonElement inner = JsonSerializer.Deserialize<JsonElement>(innerJson);
        QueuedTransaction original = new QueuedTransaction
        {
            Account = "rAcc",
            Transaction = inner,
            RetriesRemaining = 1,
            PreflightResult = "tesSUCCESS",
        };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        QueuedTransaction deserialized = JsonSerializer.Deserialize<QueuedTransaction>(json, XrplJsonOptions.Default);
        Assert.IsInstanceOfType(deserialized.Transaction, typeof(JsonElement));
        JsonElement tx = (JsonElement)deserialized.Transaction;
        Assert.AreEqual("H1", tx.GetProperty("hash").GetString());
    }

    [TestMethod]
    public void Read_InvalidTxNumber_ThrowsJsonException()
    {
        string json = @"{
            ""account"": ""rN7n7otQDd6FczFgLdlqtyMVrn3HMdfsCk"",
            ""tx"": 123,
            ""retries_remaining"": 5,
            ""preflight_result"": ""tesSUCCESS""
        }";
        Helper.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<QueuedTransaction>(json, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void Write_InvalidTransactionValue_ThrowsJsonException()
    {
        QueuedTransaction original = new QueuedTransaction
        {
            Account = "rAcc",
            Transaction = 123,
            RetriesRemaining = 1,
            PreflightResult = "tesSUCCESS",
        };
        Helper.ThrowsException<JsonException>(() =>
            JsonSerializer.Serialize(original, XrplJsonOptions.Default));
    }
}
