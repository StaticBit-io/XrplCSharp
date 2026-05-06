using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUScientificDecimalConverter
{
    private class Model
    {
        [JsonConverter(typeof(ScientificDecimalConverter))]
        [JsonPropertyName("value")]
        public decimal Value { get; set; }
    }

    private class NullableModel
    {
        [JsonPropertyName("value")]
        public decimal? Value { get; set; }
    }

    // --- Read: JSON number (plain) ---

    [TestMethod]
    public void Read_IntegerNumber_ReturnsDecimal()
    {
        string json = "{\"value\": 10}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(10m, result.Value);
    }

    [TestMethod]
    public void Read_DecimalNumber_ReturnsDecimal()
    {
        string json = "{\"value\": 2.5}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(2.5m, result.Value);
    }

    [TestMethod]
    public void Read_Zero_ReturnsZero()
    {
        string json = "{\"value\": 0}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0m, result.Value);
    }

    [TestMethod]
    public void Read_NegativeNumber_ReturnsNegativeDecimal()
    {
        string json = "{\"value\": -3.14}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(-3.14m, result.Value);
    }

    // --- Read: JSON number (scientific notation) ---

    [TestMethod]
    public void Read_ScientificNotation_SmallPositiveExponent()
    {
        string json = "{\"value\": 1e2}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(100m, result.Value);
    }

    [TestMethod]
    public void Read_ScientificNotation_NegativeExponent()
    {
        // This is the primary XRPL case: base_fee_xrp = 1e-05
        string json = "{\"value\": 1e-05}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }

    [TestMethod]
    public void Read_ScientificNotation_1e_06()
    {
        string json = "{\"value\": 1e-06}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.000001m, result.Value);
    }

    [TestMethod]
    public void Read_ScientificNotation_WithCoefficient()
    {
        string json = "{\"value\": 5.5e3}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(5500m, result.Value);
    }

    [TestMethod]
    public void Read_ScientificNotation_NegativeCoefficient()
    {
        string json = "{\"value\": -2.5e-3}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(-0.0025m, result.Value);
    }

    // --- Read: JSON string values ---

    [TestMethod]
    public void Read_StringPlainDecimal_ReturnsDecimal()
    {
        string json = "{\"value\": \"0.00001\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }

    [TestMethod]
    public void Read_StringScientificNotation_ReturnsDecimal()
    {
        string json = "{\"value\": \"1e-05\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }

    [TestMethod]
    public void Read_StringScientificNotation_UppercaseE()
    {
        string json = "{\"value\": \"1E-05\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }

    [TestMethod]
    public void Read_StringInteger_ReturnsDecimal()
    {
        string json = "{\"value\": \"42\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(42m, result.Value);
    }

    [TestMethod]
    public void Read_StringNegative_ReturnsDecimal()
    {
        string json = "{\"value\": \"-7.25\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(-7.25m, result.Value);
    }

    // --- Read: error cases ---

    [TestMethod]
    public void Read_Boolean_ThrowsJsonException()
    {
        string json = "{\"value\": true}";
        bool threw = false;
        try { JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default); }
        catch (JsonException) { threw = true; }
        Assert.IsTrue(threw, "Expected JsonException for boolean token");
    }

    [TestMethod]
    public void Read_Array_ThrowsJsonException()
    {
        string json = "{\"value\": [1,2]}";
        bool threw = false;
        try { JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default); }
        catch (JsonException) { threw = true; }
        Assert.IsTrue(threw, "Expected JsonException for array token");
    }

    [TestMethod]
    public void Read_Object_ThrowsJsonException()
    {
        string json = "{\"value\": {\"a\":1}}";
        bool threw = false;
        try { JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default); }
        catch (JsonException) { threw = true; }
        Assert.IsTrue(threw, "Expected JsonException for object token");
    }

    [TestMethod]
    public void Read_InvalidString_ThrowsException()
    {
        string json = "{\"value\": \"not_a_number\"}";
        bool threw = false;
        try { JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default); }
        catch (FormatException) { threw = true; }
        Assert.IsTrue(threw, "Expected FormatException for invalid number string");
    }

    // --- Write ---

    [TestMethod]
    public void Write_Integer_WritesNumber()
    {
        Model model = new Model { Value = 100m };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("100"));
        Assert.IsFalse(json.Contains("\"100\""));
    }

    [TestMethod]
    public void Write_Decimal_WritesNumber()
    {
        Model model = new Model { Value = 0.00001m };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("0.00001") || json.Contains("1E-05") || json.Contains("1e-05"));
    }

    [TestMethod]
    public void Write_Zero_WritesZero()
    {
        Model model = new Model { Value = 0m };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains(":0") || json.Contains(": 0"));
    }

    [TestMethod]
    public void Write_Negative_WritesNegativeNumber()
    {
        Model model = new Model { Value = -5.5m };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("-5.5"));
    }

    // --- RoundTrip ---

    [TestMethod]
    public void RoundTrip_PlainDecimal_PreservesValue()
    {
        Model original = new Model { Value = 123.456m };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(original.Value, deserialized.Value);
    }

    [TestMethod]
    public void RoundTrip_SmallValue_PreservesValue()
    {
        Model original = new Model { Value = 0.00001m };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(original.Value, deserialized.Value);
    }

    [TestMethod]
    public void RoundTrip_LargeValue_PreservesValue()
    {
        Model original = new Model { Value = 999999999.999999m };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(original.Value, deserialized.Value);
    }

    // --- XRPL-specific real-world scenarios ---

    [TestMethod]
    public void Read_XrplBaseFeeXrp_ScientificNotation()
    {
        // Real server_info response: "base_fee_xrp": 1e-05
        string json = "{\"value\": 1e-05}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }

    [TestMethod]
    public void Read_XrplReserveBaseXrp_DecimalNumber()
    {
        // Real server_info response: "reserve_base_xrp": 10.0
        string json = "{\"value\": 10.0}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(10.0m, result.Value);
    }

    [TestMethod]
    public void Read_XrplReserveIncXrp_DecimalNumber()
    {
        // Real server_info response: "reserve_inc_xrp": 2.0
        string json = "{\"value\": 2.0}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(2.0m, result.Value);
    }

    [TestMethod]
    public void Read_XrplBaseFeeXrp_PlainDecimal()
    {
        // Some server versions may return plain decimal
        string json = "{\"value\": 0.00001}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }

    [TestMethod]
    public void Read_XrplBaseFeeXrp_1e_05_AsString()
    {
        // In case the value arrives as a JSON string
        string json = "{\"value\": \"1e-05\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default)!;
        Assert.AreEqual(0.00001m, result.Value);
    }
}
