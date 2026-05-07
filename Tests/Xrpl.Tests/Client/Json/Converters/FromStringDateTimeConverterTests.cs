using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUFromStringDateTimeConverter
{
    private static readonly DateTime RippleEpoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private class Model
    {
        [JsonConverter(typeof(FromStringDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Timestamp\": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Read_Integer_ReturnsRippleEpochOffset()
    {
        string json = "{\"Timestamp\": 784111777}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        DateTime expected = RippleEpoch.AddSeconds(784111777);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Read_IsoString_ReturnsDateTime()
    {
        string json = "{\"Timestamp\": \"2024-06-15T12:30:00+00:00\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(2024, result.Timestamp.Value.Year);
        Assert.AreEqual(6, result.Timestamp.Value.Month);
        Assert.AreEqual(15, result.Timestamp.Value.Day);
    }

    [TestMethod]
    public void Read_InvalidString_ReturnsNull()
    {
        string json = "{\"Timestamp\": \"not-a-date\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Write_DateTime_WritesIsoString()
    {
        DateTime date = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        Model model = new Model { Timestamp = date };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("2024"));
        Assert.IsTrue(json.Contains("06"));
        Assert.IsTrue(json.Contains("15"));
    }

    [TestMethod]
    public void RoundTrip_IsoFormat_PreservesDate()
    {
        DateTime original = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        Model model = new Model { Timestamp = original };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(deserialized.Timestamp);
        Assert.AreEqual(original.Year, deserialized.Timestamp.Value.Year);
        Assert.AreEqual(original.Month, deserialized.Timestamp.Value.Month);
        Assert.AreEqual(original.Day, deserialized.Timestamp.Value.Day);
    }
}
