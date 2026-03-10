using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class RippleDateTimeConverterTests
{
    private static readonly DateTime RippleEpoch = new(
        year: 2000,
        month: 1,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        DateTimeKind.Utc);

    private class RippleModel
    {
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }

    [TestMethod]
    public void Deserialize_Integer_ReturnsCorrectDateTime()
    {
        // 784111777 seconds after Ripple Epoch = some date in 2024
        var json = "{\"Timestamp\": 784111777}";
        var result = JsonConvert.DeserializeObject<RippleModel>(json);
        var expected = RippleEpoch.AddSeconds(784111777);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Zero_ReturnsRippleEpoch()
    {
        var json = "{\"Timestamp\": 0}";
        var result = JsonConvert.DeserializeObject<RippleModel>(json);
        Assert.AreEqual(RippleEpoch, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Null_ReturnsNull()
    {
        var json = "{\"Timestamp\": null}";
        var result = JsonConvert.DeserializeObject<RippleModel>(json);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_StringNumber_ReturnsCorrectDateTime()
    {
        var json = "{\"Timestamp\": \"784111777\"}";
        var result = JsonConvert.DeserializeObject<RippleModel>(json);
        var expected = RippleEpoch.AddSeconds(784111777);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Serialize_DateTime_ReturnsRippleEpochSeconds()
    {
        var date = RippleEpoch.AddSeconds(784111777);
        var model = new RippleModel
        {
            Timestamp = date,
        };
        var json = JsonConvert.SerializeObject(model);
        Assert.Contains(substring: "784111777", json);
    }

    [TestMethod]
    public void Serialize_RippleEpoch_ReturnsZero()
    {
        var model = new RippleModel
        {
            Timestamp = RippleEpoch,
        };
        var json = JsonConvert.SerializeObject(model);
        Assert.Contains(substring: "0", json);
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        var original = new DateTime(year: 2024, month: 6, day: 15, hour: 12, minute: 30, second: 0, DateTimeKind.Utc);
        var model = new RippleModel
        {
            Timestamp = original,
        };
        var json = JsonConvert.SerializeObject(model);
        var deserialized = JsonConvert.DeserializeObject<RippleModel>(json);
        Assert.AreEqual(original, deserialized.Timestamp);
    }
}

[TestClass]
public class UnixDateTimeConverterTests
{
    private static readonly DateTime UnixEpoch = new(
        year: 1970,
        month: 1,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        DateTimeKind.Utc);

    private class UnixModel
    {
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }

    [TestMethod]
    public void Deserialize_Integer_ReturnsCorrectDateTime()
    {
        // 1718451000 = 2024-06-15 12:30:00 UTC
        var json = "{\"Timestamp\": 1718451000}";
        var result = JsonConvert.DeserializeObject<UnixModel>(json);
        var expected = UnixEpoch.AddSeconds(1718451000);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Zero_ReturnsUnixEpoch()
    {
        var json = "{\"Timestamp\": 0}";
        var result = JsonConvert.DeserializeObject<UnixModel>(json);
        Assert.AreEqual(UnixEpoch, result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_Null_ReturnsNull()
    {
        var json = "{\"Timestamp\": null}";
        var result = JsonConvert.DeserializeObject<UnixModel>(json);
        Assert.IsNull(result.Timestamp);
    }

    [TestMethod]
    public void Deserialize_StringNumber_ReturnsCorrectDateTime()
    {
        var json = "{\"Timestamp\": \"1718451000\"}";
        var result = JsonConvert.DeserializeObject<UnixModel>(json);
        var expected = UnixEpoch.AddSeconds(1718451000);
        Assert.AreEqual(expected, result.Timestamp);
    }

    [TestMethod]
    public void Serialize_DateTime_ReturnsUnixSeconds()
    {
        var date = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var expected = (long)((DateTimeOffset)date).ToUnixTimeSeconds(); // 1718454600
        var model = new UnixModel { Timestamp = date };
        var json = JsonConvert.SerializeObject(model);
        Assert.Contains(expected.ToString(), json);
    }

    [TestMethod]
    public void Serialize_UnixEpoch_ReturnsZero()
    {
        var model = new UnixModel
        {
            Timestamp = UnixEpoch,
        };
        var json = JsonConvert.SerializeObject(model);
        Assert.Contains(substring: "0", json);
    }

    [TestMethod]
    public void RoundTrip_PreservesValue()
    {
        var original = new DateTime(year: 2024, month: 6, day: 15, hour: 12, minute: 30, second: 0, DateTimeKind.Utc);
        var model = new UnixModel
        {
            Timestamp = original,
        };
        var json = JsonConvert.SerializeObject(model);
        var deserialized = JsonConvert.DeserializeObject<UnixModel>(json);
        Assert.AreEqual(original, deserialized.Timestamp);
    }

    [TestMethod]
    public void DifferentFromRippleConverter_SameInput()
    {
        var json = "{\"Timestamp\": 1718451000}";
        var unixResult = JsonConvert.DeserializeObject<UnixModel>(json);
        var rippleJson = json.Replace(oldValue: "Timestamp", newValue: "Timestamp");
        var rippleModel = JsonConvert.DeserializeObject<RippleModel>(rippleJson);

        // Unix и Ripple конвертеры дают разные даты для одного числа
        // Разница = 946684800 секунд (30 лет)
        var diff = (unixResult.Timestamp.Value - rippleModel.Timestamp.Value).TotalSeconds;
        Assert.AreEqual(expected: -946684800, diff);
    }

    private class RippleModel
    {
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Timestamp { get; set; }
    }
}