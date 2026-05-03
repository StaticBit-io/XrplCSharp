using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

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
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(1000000UL, result.Value);
    }

    [TestMethod]
    public void Read_Integer_ReturnsValue()
    {
        string json = "{\"Value\": 42}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(42UL, result.Value);
    }

    [TestMethod]
    public void Read_Zero_ReturnsZero()
    {
        string json = "{\"Value\": \"0\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(0UL, result.Value);
    }

    [TestMethod]
    public void Read_MaxXrplValue_ReturnsValue()
    {
        string maxVal = long.MaxValue.ToString();
        string json = $"{{\"Value\": \"{maxVal}\"}}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual((ulong)long.MaxValue, result.Value);
    }

    [TestMethod]
    public void Read_ExceedsMaxXrplValue_Throws()
    {
        ulong overflow = (ulong)long.MaxValue + 1;
        string json = $"{{\"Value\": \"{overflow}\"}}";
        bool threw = false;
        try { JsonConvert.DeserializeObject<Model>(json); }
        catch (JsonSerializationException) { threw = true; }
        Assert.IsTrue(threw, "Expected JsonSerializationException");
    }

    [TestMethod]
    public void Write_Value_ReturnsStringRepresentation()
    {
        Model model = new Model { Value = 999 };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"999\""));
    }

    [TestMethod]
    public void Write_Zero_ReturnsStringZero()
    {
        Model model = new Model { Value = 0 };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"0\""));
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Value = 123456789 };
        string json = JsonConvert.SerializeObject(original);
        Model deserialized = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(original.Value, deserialized.Value);
    }
}
