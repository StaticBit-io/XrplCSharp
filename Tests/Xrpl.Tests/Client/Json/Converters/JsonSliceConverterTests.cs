using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

/// <summary>
/// Pins that the response envelope records where <c>result</c> sits in the frame instead of
/// materializing it. The slice has to be byte-exact: everything downstream — the typed
/// deserialization and the raw JSON handed to consumers — is cut from it.
/// </summary>
[TestClass]
public class TestUJsonSliceConverter
{
    private sealed class SliceProbe
    {
        [JsonPropertyName("result")]
        [JsonConverter(typeof(JsonSliceConverter))]
        public JsonSlice Result { get; set; }
    }

    [TestMethod]
    public void TestUSliceMatchesResultSubtreeExactly()
    {
        // Deliberately irregular whitespace: the slice must reproduce the bytes as sent,
        // not a normalized rendering of them.
        string message = "{\"id\":\"7\", \"status\":\"success\", \"result\": {\"a\" : 1,\"b\":[2, 3]} , \"warning\":\"load\"}";
        byte[] frame = Encoding.UTF8.GetBytes(message);

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

        string expected = "{\"a\" : 1,\"b\":[2, 3]}";
        string actual = Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void TestUSliceIsEmptyWhenResultAbsent()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"status\":\"success\"}");

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

        Assert.IsTrue(probe.Result.IsEmpty);
    }

    [TestMethod]
    public void TestUSliceCoversExplicitNull()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"result\":null}");

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

        Assert.AreEqual("null", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
    }

    /// <summary>
    /// The only test that tells a real Skip() from counting braces by hand: both the brace and
    /// the quote live inside a string value.
    /// </summary>
    [TestMethod]
    public void TestUSliceSkipsBracesInsideStrings()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"m\":\"}{\\\"\"},\"x\":2}");

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

        Assert.AreEqual("{\"m\":\"}{\\\"\"}", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
    }

    /// <summary>Offsets are counted in bytes, not characters.</summary>
    [TestMethod]
    public void TestUSliceOffsetsAreByteBased()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"pad\":\"é中😀\",\"result\":{\"a\":1}}");

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

        Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
    }

    [TestMethod]
    public void TestUWritingASliceIsRejected()
    {
        SliceProbe probe = new SliceProbe { Result = new JsonSlice(0, 2) };

        Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Serialize(probe, new JsonSerializerOptions()));
    }

    /// <summary>
    /// The production path runs on XrplJsonOptions.Default, which carries three dozen
    /// converters; the bounds must not depend on the bare options used elsewhere here.
    /// </summary>
    [TestMethod]
    public void TestUSliceIsTheSameUnderProductionOptions()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"result\":{\"a\":1},\"x\":2}");

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, XrplJsonOptions.Default);

        Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
    }

    /// <summary>
    /// A frame large enough that a chunked reader would drift. Small fixtures happen to give
    /// the right offsets even off a stream, so only a big one pins the contract.
    /// </summary>
    [TestMethod]
    public void TestUSliceStaysExactOnALargeFrame()
    {
        StringBuilder builder = new StringBuilder(48 * 1024);
        builder.Append("{\"pad\":\"").Append('x', 40 * 1024).Append("\",\"result\":{\"a\":1}}");
        byte[] frame = Encoding.UTF8.GetBytes(builder.ToString());

        SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, XrplJsonOptions.Default);

        Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
    }
}
