using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestULedgerIndexConverter
{
    private class Model
    {
        [JsonConverter(typeof(LedgerIndexConverter))]
        public LedgerIndex Ledger { get; set; }
    }

    [TestMethod]
    public void Write_NumericIndex_WritesNumber()
    {
        Model model = new Model { Ledger = new LedgerIndex(12345) };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("12345"));
        Assert.IsFalse(json.Contains("\"12345\""));
    }

    [TestMethod]
    public void Write_Validated_WritesString()
    {
        Model model = new Model { Ledger = new LedgerIndex(LedgerIndexType.Validated) };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"validated\""));
    }

    [TestMethod]
    public void Write_Current_WritesString()
    {
        Model model = new Model { Ledger = new LedgerIndex(LedgerIndexType.Current) };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"current\""));
    }

    [TestMethod]
    public void Write_Closed_WritesString()
    {
        Model model = new Model { Ledger = new LedgerIndex(LedgerIndexType.Closed) };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"closed\""));
    }

    [TestMethod]
    public void Read_NumericIndex_ReturnsLedgerIndex()
    {
        string json = "{\"Ledger\": 12345}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Ledger);
        Assert.AreEqual(12345u, result.Ledger.Index);
    }

    /// <summary>
    /// A ledger index sent as a string - some nodes and proxies quote it - is the index, not an
    /// enum value: Enum.TryParse accepts integer strings, so it used to come back as an undefined
    /// <see cref="LedgerIndexType"/> with no index.
    /// </summary>
    [TestMethod]
    public void Read_NumericString_ReturnsLedgerIndex()
    {
        Model result = JsonSerializer.Deserialize<Model>("{\"Ledger\": \"12345\"}", XrplJsonOptions.Default);
        Assert.AreEqual(12345u, result.Ledger.Index);
    }

    [TestMethod]
    public void Read_ShortcutString_ReturnsLedgerIndexType()
    {
        Model result = JsonSerializer.Deserialize<Model>("{\"Ledger\": \"Validated\"}", XrplJsonOptions.Default);
        Assert.IsNull(result.Ledger.Index);
        Assert.AreEqual(LedgerIndexType.Validated, result.Ledger.LedgerIndexType);
    }

    [DataTestMethod]
    [DataRow("-1")]
    [DataRow("99999999999")]
    [DataRow(" 12")]
    [DataRow("latest")]
    public void Read_InvalidString_Throws(string value)
    {
        string json = "{\"Ledger\": \"" + value + "\"}";
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default));
    }
}
