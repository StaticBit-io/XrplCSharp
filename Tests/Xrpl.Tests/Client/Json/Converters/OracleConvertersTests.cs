using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

using XrplTests;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUAssetPriceConverter
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
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(255UL, result.Price);
    }

    [TestMethod]
    public void Read_Integer_ReturnsUlong()
    {
        string json = "{\"Price\": 100}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(100UL, result.Price);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Price\": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Price);
    }

    [TestMethod]
    public void Write_Value_WritesLowercaseHex()
    {
        Model model = new Model { Price = 255UL };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"ff\""));
    }

    [TestMethod]
    public void Write_Null_OmitsProperty()
    {
        Model model = new Model { Price = null };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsFalse(json.Contains("Price"), "Null properties should be omitted with WhenWritingNull");
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Price = 0xABCDUL };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(0xABCDUL, deserialized.Price);
    }
}

[TestClass]
public class TestUOracleCurrencyConverter
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
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("USD", result.Currency);
    }

    [TestMethod]
    public void Read_HexCode40Chars_DecodesToString()
    {
        string hex = "5553440000000000000000000000000000000000"; // "USD" padded to 40 chars
        string json = $"{{\"Currency\": \"{hex}\"}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("USD", result.Currency);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Currency\": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Currency);
    }

    [TestMethod]
    public void Write_ShortCode_WritesAsIs()
    {
        Model model = new Model { Currency = "XRP" };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"XRP\""));
    }

    [TestMethod]
    public void Write_LongCode_WritesHex40Chars()
    {
        Model model = new Model { Currency = "Bitcoin" };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        // "Bitcoin" (7 chars) should be padded to 40 hex chars (Hashes.CurrencyToHex)
        string expected = "426974636f696e00000000000000000000000000";
        Assert.IsTrue(json.Contains(expected));
    }

    [TestMethod]
    public void Write_TwoCharCode_EncodesAsHex40Chars()
    {
        Model model = new Model { Currency = "BT" };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        string expected = "4254000000000000000000000000000000000000";
        Assert.IsTrue(json.Contains(expected), "Non-standard codes (not exactly 3 chars) must use 40-char hex.");
    }

    [TestMethod]
    public void Write_NonAsciiCurrency_ThrowsJsonException()
    {
        Model model = new Model { Currency = "€UR" };
        Helper.ThrowsException<JsonException>(() => JsonSerializer.Serialize(model, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void RoundTrip_ShortCode_Preserved()
    {
        Model original = new Model { Currency = "BTC" };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("BTC", deserialized.Currency);
    }
}

[TestClass]
public class TestUOracleHexStringConverter
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
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("test", result.Provider);
    }

    [TestMethod]
    public void Read_HexDecodedNonPrintable_ThrowsJsonException()
    {
        string json = "{\"Provider\": \"01\"}";
        Helper.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Provider\": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Provider);
    }

    [TestMethod]
    public void Read_PlainText_ReturnsAsIs()
    {
        // "abc" has odd length, not treated as hex
        string json = "{\"Provider\": \"abc\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("abc", result.Provider);
    }

    [TestMethod]
    public void Write_PlainText_WritesHex()
    {
        Model model = new Model { Provider = "test" };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("74657374"));
    }

    [TestMethod]
    public void Write_HexLookingText_EncodesAsAscii()
    {
        Model model = new Model { Provider = "74657374" };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        // "74657374" is treated as plain ASCII text, each char encoded to hex
        Assert.IsFalse(json.Contains("\"74657374\""));
        Assert.IsTrue(json.Contains("3734363537333734"));
    }

    [TestMethod]
    public void Write_NonAsciiProvider_ThrowsJsonException()
    {
        Model model = new Model { Provider = "тест" };
        Helper.ThrowsException<JsonException>(() => JsonSerializer.Serialize(model, XrplJsonOptions.Default));
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        Model original = new Model { Provider = "oracle_provider" };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("oracle_provider", deserialized.Provider);
    }
}
