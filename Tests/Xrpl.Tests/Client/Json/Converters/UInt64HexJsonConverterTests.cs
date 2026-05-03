using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class UInt64HexJsonConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(UInt64HexJsonConverter))]
        public ulong Value { get; set; }
    }

    [TestMethod]
    public void Read_HexString_ReturnsValue()
    {
        string json = "{\"Value\": \"FF\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(255UL, result.Value);
    }

    [TestMethod]
    public void Read_HexStringLowerCase_ReturnsValue()
    {
        string json = "{\"Value\": \"ff\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(255UL, result.Value);
    }

    [TestMethod]
    public void Read_HexStringZero_ReturnsZero()
    {
        string json = "{\"Value\": \"0\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(0UL, result.Value);
    }

    [TestMethod]
    public void Read_Integer_ReturnsValue()
    {
        string json = "{\"Value\": 100}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(100UL, result.Value);
    }

    [TestMethod]
    public void Read_LargeHex_ReturnsValue()
    {
        string json = "{\"Value\": \"FFFFFFFFFFFFFFFF\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(ulong.MaxValue, result.Value);
    }

    [TestMethod]
    public void Read_Null_Throws()
    {
        string json = "{\"Value\": null}";
        bool threw = false;
        try { JsonConvert.DeserializeObject<Model>(json); }
        catch (JsonSerializationException) { threw = true; }
        Assert.IsTrue(threw, "Expected JsonSerializationException");
    }

    [TestMethod]
    public void Write_Value_ReturnsUppercaseHex()
    {
        Model model = new Model { Value = 255 };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"FF\""));
    }

    [TestMethod]
    public void Write_Zero_ReturnsZeroHex()
    {
        Model model = new Model { Value = 0 };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"0\""));
    }

    [TestMethod]
    public void Write_LargeValue_ReturnsFullHex()
    {
        Model model = new Model { Value = 0xDEADBEEF };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"DEADBEEF\""));
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Value = 0xABCD1234 };
        string json = JsonConvert.SerializeObject(original);
        Model deserialized = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(original.Value, deserialized.Value);
    }
}
