using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class CurrencyConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }
    }

    [TestMethod]
    public void Read_XrpDropsString_ReturnsCurrencyXrp()
    {
        string json = "{\"Amount\": \"1000000\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Amount);
        Assert.AreEqual("XRP", result.Amount.CurrencyCode);
        Assert.AreEqual("1000000", result.Amount.Value);
    }

    [TestMethod]
    public void Read_XrpDropsInteger_ReturnsCurrencyXrp()
    {
        string json = "{\"Amount\": 500000}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Amount);
        Assert.AreEqual("XRP", result.Amount.CurrencyCode);
        Assert.AreEqual("500000", result.Amount.Value);
    }

    [TestMethod]
    public void Read_IouObject_ReturnsCurrencyWithIssuer()
    {
        string json = @"{""Amount"": {
            ""currency"": ""USD"",
            ""issuer"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""value"": ""100.50""
        }}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Amount);
        Assert.AreEqual("USD", result.Amount.CurrencyCode);
        Assert.AreEqual("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", result.Amount.Issuer);
        Assert.AreEqual("100.50", result.Amount.Value);
    }

    [TestMethod]
    public void Read_MptObject_ReturnsCurrencyWithMptId()
    {
        string json = @"{""Amount"": {
            ""mpt_issuance_id"": ""00000001A407AF5856CDF13C0E7B6EDA1A249768ADC1F5E1"",
            ""value"": ""50""
        }}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Amount);
        Assert.AreEqual("00000001A407AF5856CDF13C0E7B6EDA1A249768ADC1F5E1", result.Amount.MPTokenIssuanceID);
        Assert.AreEqual("50", result.Amount.Value);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Amount\": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Amount);
    }

    [TestMethod]
    public void Write_XrpDrops_WritesStringValue()
    {
        Model model = new Model
        {
            Amount = new Currency { CurrencyCode = "XRP", Value = "1000000" }
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"1000000\""));
        Assert.IsFalse(json.Contains("currency"));
    }

    [TestMethod]
    public void Write_Iou_WritesObject()
    {
        Model model = new Model
        {
            Amount = new Currency
            {
                CurrencyCode = "USD",
                Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                Value = "100"
            }
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"currency\""));
        Assert.IsTrue(json.Contains("\"USD\""));
        Assert.IsTrue(json.Contains("\"issuer\""));
    }

    [TestMethod]
    public void Write_Mpt_WritesMptObject()
    {
        Model model = new Model
        {
            Amount = new Currency
            {
                MPTokenIssuanceID = "00000001A407AF5856CDF13C0E7B6EDA1A249768ADC1F5E1",
                Value = "50"
            }
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("mpt_issuance_id"));
        Assert.IsTrue(json.Contains("00000001A407AF5856CDF13C0E7B6EDA1A249768ADC1F5E1"));
    }

    [TestMethod]
    public void RoundTrip_XrpDrops_PreservesValue()
    {
        Model original = new Model
        {
            Amount = new Currency { CurrencyCode = "XRP", Value = "5000000" }
        };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("XRP", deserialized.Amount.CurrencyCode);
        Assert.AreEqual("5000000", deserialized.Amount.Value);
    }

    [TestMethod]
    public void RoundTrip_Iou_PreservesValue()
    {
        Model original = new Model
        {
            Amount = new Currency
            {
                CurrencyCode = "EUR",
                Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                Value = "42.5"
            }
        };
        string json = JsonSerializer.Serialize(original, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.AreEqual("EUR", deserialized.Amount.CurrencyCode);
        Assert.AreEqual("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", deserialized.Amount.Issuer);
        Assert.AreEqual("42.5", deserialized.Amount.Value);
    }
}

[TestClass]
public class IssuedCurrencyConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public Common.IssuedCurrency Asset { get; set; }
    }

    [TestMethod]
    public void Read_Object_ReturnsIssuedCurrency()
    {
        string json = @"{""Asset"": {""currency"": ""USD"", ""issuer"": ""rAddr""}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Asset);
        Assert.AreEqual("USD", result.Asset.Currency);
    }

    [TestMethod]
    public void Read_String_ReturnsXrp()
    {
        string json = @"{""Asset"": ""XRP""}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Asset);
        Assert.AreEqual("XRP", result.Asset.Currency);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = @"{""Asset"": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Asset);
    }

    [TestMethod]
    public void Write_Xrp_WritesXrpObject()
    {
        Model model = new Model
        {
            Asset = new Common.IssuedCurrency { Currency = "XRP" }
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"XRP\""));
    }
}
