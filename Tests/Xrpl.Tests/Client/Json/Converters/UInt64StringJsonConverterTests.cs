using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class UInt64StringJsonConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(UInt64StringJsonConverter))]
        public ulong Value { get; set; }
    }

    [TestMethod]
    public void Read_StringNumber_ReturnsValue()
    {
        string json = "{\"Value\": \"1000000\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(1000000UL, result.Value);
    }

    [TestMethod]
    public void Read_Integer_ReturnsValue()
    {
        string json = "{\"Value\": 42}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(42UL, result.Value);
    }

    [TestMethod]
    public void Read_Zero_ReturnsZero()
    {
        string json = "{\"Value\": \"0\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(0UL, result.Value);
    }

    [TestMethod]
    public void Read_MaxXrplValue_ReturnsValue()
    {
        string maxVal = long.MaxValue.ToString();
        string json = $"{{\"Value\": \"{maxVal}\"}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual((ulong)long.MaxValue, result.Value);
    }

    [TestMethod]
    public void Read_ExceedsMaxXrplValue_Throws()
    {
        ulong overflow = (ulong)long.MaxValue + 1;
        string json = $"{{\"Value\": \"{overflow}\"}}";
        bool threw = false;
        try { JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default); }
        catch (JsonException) { threw = true; }
        Assert.IsTrue(threw, "Expected JsonException");
    }

    [TestMethod]
    public void Write_Value_ReturnsStringRepresentation()
    {
        Model model = new Model { Value = 999 };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"999\""));
    }

    [TestMethod]
    public void Write_Zero_ReturnsStringZero()
    {
        Model model = new Model { Value = 0 };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"0\""));
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Value = 123456789 };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(original.Value, deserialized.Value);
    }
}
