using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

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
        string hex = "426974636F696E00000000000000000000000000"; // "Bitcoin" padded to 40 chars
        string json = $"{{\"Currency\": \"{hex}\"}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("Bitcoin", result.Currency);
    }

    /// <summary>
    /// Three characters at byte 0 are a nonstandard code. Read as "USD", they would be written back
    /// as the standard code USD (bytes 12-14), a different currency, so they stay hex.
    /// </summary>
    [TestMethod]
    public void Read_HexCode40Chars_ThreeCharsAtByteZero_StaysHex()
    {
        string hex = "5553440000000000000000000000000000000000";
        string json = $"{{\"Currency\": \"{hex}\"}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(hex, result.Currency);

        string written = JsonSerializer.Serialize(result, XrplJsonOptions.Default);
        StringAssert.Contains(written, hex);
    }

    /// <summary>
    /// A currency code is 160 bits the ledger does not constrain, so a nonstandard one that is
    /// not text comes back as the hex the node sent rather than throwing out of the converter.
    /// </summary>
    [TestMethod]
    public void Read_HexCode40Chars_NonPrintable_ReturnsHexUnchanged()
    {
        string hex = "0158415500000000C1F76FF6ECB0BAC600000000"; // legacy demurrage code
        string json = $"{{\"Currency\": \"{hex}\"}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual(hex, result.Currency);
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
        // "Bitcoin" (7 chars) should be padded to 40 hex chars (Hashes.CurrencyToHex).
        // UPPERCASE: the SDK emits hex in the same case rippled returns it
        string expected = "426974636F696E00000000000000000000000000";
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
public class TestUOracleBlobConverter
{
    private class Model
    {
        public OracleBlob Provider { get; set; }
    }

    private static Model Read(string json) => JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);

    private static string Write(Model model) => JsonSerializer.Serialize(model, XrplJsonOptions.Default);

    [TestMethod]
    public void Read_PrintableHex_IsText()
    {
        Model result = Read("{\"Provider\": \"74657374\"}");
        Assert.IsTrue(result.Provider.TryGetText(out string text));
        Assert.AreEqual("test", text);
    }

    /// <summary>
    /// Provider, AssetClass and URI are Blob fields; rippled checks their length and nothing
    /// else (OracleSet::preflight), so an oracle published by anyone may hold arbitrary bytes.
    /// Refusing one threw out of the converter and took the whole response with it: a single
    /// third-party oracle made a ledger_data page from devnet unreadable.
    /// </summary>
    [TestMethod]
    public void Read_NonPrintableHex_KeepsBytes()
    {
        Model result = Read("{\"Provider\": \"C328FF\"}");
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x28, 0xFF }, result.Provider.ToArray());
        Assert.IsFalse(result.Provider.TryGetText(out string text));
        Assert.IsNull(text);
    }

    /// <summary>
    /// The text "C328FF" and the bytes C3 28 FF were one model value, and both were written as
    /// the hex digits encoded as text (#272).
    /// </summary>
    [DataTestMethod]
    [DataRow("70726F7669646572")]
    [DataRow("C328FF")]
    [DataRow("433332384646")]
    [DataRow("01")]
    [DataRow("")]
    public void RoundTrip_WritesTheBytesRead(string hex)
    {
        string written = Write(Read($"{{\"Provider\": \"{hex}\"}}"));
        Assert.AreEqual($"{{\"Provider\":\"{hex}\"}}", written);
    }

    [TestMethod]
    public void RoundTrip_LowercaseHex_WritesUppercase()
    {
        Assert.AreEqual("{\"Provider\":\"C328FF\"}", Write(Read("{\"Provider\": \"c328ff\"}")));
    }

    [TestMethod]
    public void Read_TextAndBytesWithSameDigits_AreDifferentValues()
    {
        Model text = Read("{\"Provider\": \"433332384646\"}");
        Model bytes = Read("{\"Provider\": \"C328FF\"}");
        Assert.AreNotEqual(text.Provider, bytes.Provider);
        Assert.AreEqual("433332384646", text.Provider.ToString());
        Assert.AreEqual("C328FF", bytes.Provider.ToString());
    }

    [DataTestMethod]
    [DataRow("\"ABC\"")]
    [DataRow("\"provider\"")]
    [DataRow("\"0x01\"")]
    [DataRow("12")]
    public void Read_NotHexString_Throws(string value)
    {
        Helper.ThrowsException<JsonException>(() => Read($"{{\"Provider\": {value}}}"));
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        Assert.IsNull(Read("{\"Provider\": null}").Provider);
    }

    [TestMethod]
    public void Write_Text_WritesItsAsciiBytes()
    {
        Assert.AreEqual("{\"Provider\":\"74657374\"}", Write(new Model { Provider = "test" }));
    }

    /// <summary>
    /// A string assigned to the field is text, whatever it looks like; bytes given as hex go
    /// through FromHex.
    /// </summary>
    [TestMethod]
    public void Write_HexLookingText_IsText_FromHexIsBytes()
    {
        Assert.AreEqual("{\"Provider\":\"3734363537333734\"}", Write(new Model { Provider = "74657374" }));
        Assert.AreEqual("{\"Provider\":\"74657374\"}", Write(new Model { Provider = OracleBlob.FromHex("74657374") }));
    }

    [TestMethod]
    public void Text_NonAscii_Throws()
    {
        Helper.ThrowsException<ArgumentException>(() => { OracleBlob _ = "тест"; });
    }

    [TestMethod]
    public void FromHex_OddLengthOrNotHex_Throws()
    {
        Helper.ThrowsException<FormatException>(() => OracleBlob.FromHex("ABC"));
        Helper.ThrowsException<FormatException>(() => OracleBlob.FromHex("ZZ"));
    }

    [TestMethod]
    public void Equality_ComparesBytes()
    {
        OracleBlob a = OracleBlob.FromHex("74657374");
        OracleBlob b = "test";
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsTrue(a != OracleBlob.FromHex("7465737400"));
    }

    [TestMethod]
    public void RoundTrip_Text_PreservesValue()
    {
        Model deserialized = Read(Write(new Model { Provider = "oracle_provider" }));
        Assert.IsTrue(deserialized.Provider.TryGetText(out string text));
        Assert.AreEqual("oracle_provider", text);
    }
}
