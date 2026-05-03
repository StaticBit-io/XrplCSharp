using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class FromStringDateTimeConverterTests
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
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Read_Integer_ReturnsRippleEpochOffset()
    {
        string json = "{\"Timestamp\": 784111777}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        DateTime expected = RippleEpoch.AddSeconds(784111777);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Read_IsoString_ReturnsDateTime()
    {
        string json = "{\"Timestamp\": \"2024-06-15T12:30:00+00:00\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(2024, result.Timestamp.Value.Year);
        Assert.AreEqual(6, result.Timestamp.Value.Month);
        Assert.AreEqual(15, result.Timestamp.Value.Day);
    }

    [TestMethod]
    public void Read_InvalidString_ReturnsNull()
    {
        string json = "{\"Timestamp\": \"not-a-date\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Write_DateTime_WritesIsoString()
    {
        DateTime date = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        Model model = new Model { Timestamp = date };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("2024"));
        Assert.IsTrue(json.Contains("06"));
        Assert.IsTrue(json.Contains("15"));
    }

    [TestMethod]
    public void RoundTrip_IsoFormat_PreservesDate()
    {
        DateTime original = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        Model model = new Model { Timestamp = original };
        string json = JsonConvert.SerializeObject(model);
        Model deserialized = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNotNull(deserialized.Timestamp);
        Assert.AreEqual(original.Year, deserialized.Timestamp.Value.Year);
        Assert.AreEqual(original.Month, deserialized.Timestamp.Value.Month);
        Assert.AreEqual(original.Day, deserialized.Timestamp.Value.Day);
    }
}
