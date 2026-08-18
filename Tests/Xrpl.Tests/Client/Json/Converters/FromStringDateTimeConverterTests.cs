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

    /// <summary>
    /// Real mainnet close_time_iso values are "Z"-suffixed (Zulu time), not the numeric-offset
    /// form covered by <see cref="Read_IsoString_ReturnsDateTime"/>. Value captured from a live
    /// tx response (rippled hash E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7).
    /// Before the converter accepted "K" instead of "zzz", TryParseExact failed on this shape and
    /// the converter silently returned null — every close_time_iso on a real response was lost,
    /// even on the models that already declared the property.
    /// </summary>
    [TestMethod]
    public void Read_ZSuffixedIsoString_ReturnsDateTime()
    {
        string json = "{\"Timestamp\": \"2013-03-12T23:16:50Z\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Timestamp, "\"Z\"-suffixed timestamps must parse, not silently become null");
        Assert.AreEqual(new DateTime(2013, 3, 12, 23, 16, 50, DateTimeKind.Utc), result.Timestamp.Value);
        Assert.AreEqual(DateTimeKind.Utc, result.Timestamp.Value.Kind);
    }

    /// <summary>
    /// A numeric offset that is not already UTC must be converted, not merely reinterpreted as UTC.
    /// </summary>
    [TestMethod]
    public void Read_NonUtcOffset_AdjustsToUtc()
    {
        string json = "{\"Timestamp\": \"2013-03-12T23:16:50+02:00\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(new DateTime(2013, 3, 12, 21, 16, 50, DateTimeKind.Utc), result.Timestamp.Value);
    }

    /// <summary>Round-trip must not regress to the old "+00:00" write format silently losing "Z" input.</summary>
    [TestMethod]
    public void RoundTrip_ZSuffixedIsoString_WritesZSuffixBack()
    {
        string json = "{\"Timestamp\": \"2013-03-12T23:16:50Z\"}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);

        string output = JsonSerializer.Serialize(result, XrplJsonOptions.Default);

        StringAssert.Contains(output, "2013-03-12T23:16:50Z");
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

    /// <summary>
    /// A Utc-kind value is the baseline Read itself always produces: "K" must emit the "Z" suffix,
    /// not a "+00:00" offset.
    /// </summary>
    [TestMethod]
    public void Write_UtcKind_WritesZSuffix()
    {
        DateTime date = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        Model model = new Model { Timestamp = date };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        StringAssert.Contains(json, "2024-06-15T12:30:00Z");
    }

    /// <summary>
    /// "K" emits no zone marker at all for DateTimeKind.Unspecified - neither "Z" nor a numeric
    /// offset - so an unset Kind must be normalized to UTC before formatting, mirroring
    /// DateTimeStyles.AssumeUniversal on the Read side. A caller assigning a DateTime by hand
    /// (Read itself never produces Unspecified) is exactly the case this covers.
    /// </summary>
    [TestMethod]
    public void Write_UnspecifiedKind_TreatedAsUtc()
    {
        DateTime date = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Unspecified);
        Model model = new Model { Timestamp = date };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        StringAssert.Contains(json, "2024-06-15T12:30:00Z");
    }

    /// <summary>
    /// A Local-kind value must be converted to UTC before formatting, not written out with a local
    /// offset that Read (which always normalizes to UTC) would then interpret differently on the
    /// way back in.
    /// </summary>
    [TestMethod]
    public void Write_LocalKind_ConvertsToUtc()
    {
        DateTime utc = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        DateTime local = utc.ToLocalTime();
        Model model = new Model { Timestamp = local };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);

        Model deserialized = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(deserialized.Timestamp);
        Assert.AreEqual(DateTimeKind.Utc, deserialized.Timestamp.Value.Kind);
        Assert.AreEqual(utc, deserialized.Timestamp.Value);
    }
}
