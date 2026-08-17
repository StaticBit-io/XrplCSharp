using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

using Xrpl.Client.Json;

namespace XrplTests.Client.Json;

/// <summary>
/// Pins the contract of the window a consumer is handed onto the bytes a node actually sent:
/// it aliases the frame rather than copying it, detaches only through <see cref="RawJson.ToArray"/>,
/// rejects a window that does not lie inside its frame, and round-trips through a
/// <see cref="Utf8JsonWriter"/> byte-for-byte — including the zero-length window an absent
/// response member produces.
/// </summary>
[TestClass]
public class TestURawJson
{
    [TestMethod]
    public void TestURawJsonRendersTheOriginalBytes()
    {
        // `{"result": {"a" : 1} }` — the inner object starts at byte 11 and is 9 bytes long.
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\": {\"a\" : 1} }");
        RawJson raw = new RawJson(frame, 11, 9);

        Assert.AreEqual("{\"a\" : 1}", raw.ToString());
        Assert.AreEqual(9, raw.Length);
        Assert.IsFalse(raw.IsEmpty);
    }

    [TestMethod]
    public void TestURawJsonDefaultIsEmpty()
    {
        RawJson raw = default;

        Assert.IsTrue(raw.IsEmpty);
        Assert.AreEqual(string.Empty, raw.ToString());
        Assert.AreEqual(0, raw.Span.Length);
        Assert.AreEqual(0, raw.Length);
        Assert.AreEqual(0, default(RawJson).ToArray().Length);
    }

    [TestMethod]
    public void TestURawJsonSpanAliasesTheFrame()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"a\":1}}");
        RawJson raw = new RawJson(frame, 10, 7);

        frame[12] = (byte)'b';

        // Not a copy: the window addresses the frame's own bytes, so the frame's mutation shows.
        Assert.AreEqual("{\"b\":1}", raw.ToString());
    }

    [TestMethod]
    public void TestURawJsonToArrayDetachesFromTheFrame()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"a\":1}}");
        byte[] copy = new RawJson(frame, 10, 7).ToArray();

        frame[12] = (byte)'b';

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("{\"a\":1}"), copy);
    }

    /// <summary>The shape an absent member produces: a live frame with a zero-length window.</summary>
    [TestMethod]
    public void TestURawJsonZeroLengthWindowIsEmpty()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\"}");
        RawJson raw = new RawJson(frame, 0, 0);

        Assert.IsTrue(raw.IsEmpty);
        Assert.AreEqual(0, raw.Length);
        Assert.AreEqual(string.Empty, raw.ToString());
    }

    [TestMethod]
    public void TestURawJsonWriteToEmitsTheBytesVerbatim()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\": {\"a\" : 1,\"b\":[2, 3]} }");
        RawJson raw = new RawJson(frame, 11, 20);

        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("result");
            raw.WriteTo(writer);
            writer.WriteEndObject();
        }

        Assert.AreEqual("{\"result\":{\"a\" : 1,\"b\":[2, 3]}}", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    /// <summary>Regression: a zero-length window used to reach WriteRawValue and throw.</summary>
    [TestMethod]
    public void TestURawJsonWriteToEmitsNullForAnEmptyWindow()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\"}");

        Assert.AreEqual("null", Write(new RawJson(frame, 0, 0)));
        Assert.AreEqual("null", Write(default));

        static string Write(RawJson raw)
        {
            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
            {
                raw.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }

    [TestMethod]
    public void TestURawJsonWriteToRejectsANullWriter()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new RawJson(null, 0, 0).WriteTo(null));
    }

    [TestMethod]
    public void TestURawJsonRejectsAWindowOutsideTheFrame()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"a\":1}");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RawJson(frame, 3, 99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RawJson(frame, -1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RawJson(frame, 0, -2));
    }

    /// <summary>Length is bytes, not characters.</summary>
    [TestMethod]
    public void TestURawJsonLengthIsInBytes()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"v\":\"é中😀\"}");
        RawJson raw = new RawJson(frame, 5, frame.Length - 6);

        Assert.AreEqual("\"é中😀\"", raw.ToString());
        Assert.AreEqual(frame.Length - 6, raw.Length);
    }
}
