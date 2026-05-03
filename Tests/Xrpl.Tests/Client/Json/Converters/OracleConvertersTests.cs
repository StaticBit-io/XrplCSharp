using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class AssetPriceConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(AssetPriceConverter))]
        public object Price { get; set; }
    }

    [TestMethod]
    public void Read_HexString_ReturnsUlong()
    {
        string json = "{\"Price\": \"ff\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(255UL, result.Price);
    }

    [TestMethod]
    public void Read_Integer_ReturnsUlong()
    {
        string json = "{\"Price\": 100}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(100UL, result.Price);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Price\": null}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNull(result.Price);
    }

    [TestMethod]
    public void Write_Value_WritesLowercaseHex()
    {
        Model model = new Model { Price = 255UL };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"ff\""));
    }

    [TestMethod]
    public void Write_Null_WritesNull()
    {
        Model model = new Model { Price = null };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("null"));
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Price = 0xABCDUL };
        string json = JsonConvert.SerializeObject(original);
        Model deserialized = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual(0xABCDUL, deserialized.Price);
    }
}

[TestClass]
public class OracleCurrencyConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(OracleCurrencyConverter))]
        public string Currency { get; set; }
    }

    [TestMethod]
    public void Read_ShortCode_ReturnsAsIs()
    {
        string json = "{\"Currency\": \"USD\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("USD", result.Currency);
    }

    [TestMethod]
    public void Read_HexCode40Chars_DecodesToString()
    {
        string hex = "5553440000000000000000000000000000000000"; // "USD" padded to 40 chars
        string json = $"{{\"Currency\": \"{hex}\"}}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("USD", result.Currency);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Currency\": null}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNull(result.Currency);
    }

    [TestMethod]
    public void Write_ShortCode_WritesAsIs()
    {
        Model model = new Model { Currency = "XRP" };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"XRP\""));
    }

    [TestMethod]
    public void Write_LongCode_WritesHex40Chars()
    {
        Model model = new Model { Currency = "Bitcoin" };
        string json = JsonConvert.SerializeObject(model);
        // "Bitcoin" (7 chars) should be padded to 40 hex chars
        string expected = "426974636f696e00000000000000000000000000";
        Assert.IsTrue(json.Contains(expected));
    }

    [TestMethod]
    public void RoundTrip_ShortCode_Preserved()
    {
        Model original = new Model { Currency = "BTC" };
        string json = JsonConvert.SerializeObject(original);
        Model deserialized = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("BTC", deserialized.Currency);
    }
}

[TestClass]
public class OracleHexStringConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(OracleHexStringConverter))]
        public string Provider { get; set; }
    }

    [TestMethod]
    public void Read_HexEncoded_DecodesToAscii()
    {
        // "test" in hex = 74657374
        string json = "{\"Provider\": \"74657374\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("test", result.Provider);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Provider\": null}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNull(result.Provider);
    }

    [TestMethod]
    public void Read_PlainText_ReturnsAsIs()
    {
        // "abc" has odd length, not treated as hex
        string json = "{\"Provider\": \"abc\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("abc", result.Provider);
    }

    [TestMethod]
    public void Write_PlainText_WritesHex()
    {
        Model model = new Model { Provider = "test" };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("74657374"));
    }

    [TestMethod]
    public void Write_AlreadyHex_PassesThrough()
    {
        Model model = new Model { Provider = "74657374" };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("74657374"));
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Provider = "oracle_provider" };
        string json = JsonConvert.SerializeObject(original);
        Model deserialized = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("oracle_provider", deserialized.Provider);
    }
}
