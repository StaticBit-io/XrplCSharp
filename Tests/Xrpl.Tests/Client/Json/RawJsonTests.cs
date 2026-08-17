using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;

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

    /// <summary>
    /// Equality is identity of the window, not of the bytes: same frame, same bounds. Comparing
    /// content is what Span is for. Pinned here because this is a public contract — changing it
    /// later is a breaking change, and an untested contract drifts just as quietly as an unstated one.
    /// </summary>
    [TestMethod]
    public void TestURawJsonEqualityIsIdentityOfTheWindow()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"a\":1}");
        byte[] twin = Encoding.UTF8.GetBytes("{\"a\":1}");

        Assert.IsTrue(new RawJson(frame, 0, 3) == new RawJson(frame, 0, 3));
        Assert.IsTrue(new RawJson(frame, 0, 3) != new RawJson(frame, 3, 3));
        Assert.IsTrue(new RawJson(frame, 0, 3) != new RawJson(twin, 0, 3));
        Assert.IsTrue(default(RawJson) == default(RawJson));
        Assert.IsFalse(new RawJson(frame, 0, 3).Equals("not a RawJson"));

        // The bounds have to reach the hash: the default struct hash used the frame reference alone,
        // so two different windows onto one frame collided.
        Assert.AreNotEqual(new RawJson(frame, 0, 3).GetHashCode(), new RawJson(frame, 3, 3).GetHashCode());
    }

    /// <summary>
    /// The payload is deliberately awkward: the key differs in case and the number arrives as a
    /// string. Both are read only because XrplJsonOptions.Default sets PropertyNameCaseInsensitive
    /// and AllowReadingFromString — under bare options this deserializes to zero, which is what
    /// makes the test able to tell the two apart.
    /// </summary>
    [TestMethod]
    public void TestURawJsonDeserializesWithLibraryOptions()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"Ledger_Index\":\"9\",\"marker\":\"AABB\"}}");
        RawJson raw = new RawJson(frame, 10, frame.Length - 11);

        LOLedgerData typed = raw.Deserialize<LOLedgerData>();

        Assert.IsNotNull(typed);
        Assert.AreEqual(9u, typed.LedgerIndex);
        Assert.AreEqual("AABB", typed.Marker.ToString());
    }

    [TestMethod]
    public void TestURawJsonDeserializeOnAnEmptyWindowReturnsDefault()
    {
        Assert.IsNull(default(RawJson).Deserialize<LOLedgerData>());
    }

    [TestMethod]
    public void TestURawJsonToJsonElementOwnsItsData()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"a\":1}}");
        JsonElement element = new RawJson(frame, 10, 7).ToJsonElement();

        // Wipe the whole window the element was parsed from, not just one byte: if the element
        // aliased the frame instead of copying out of it, this would corrupt every field it reads.
        Array.Clear(frame, 10, 7);

        Assert.AreEqual(1, element.GetProperty("a").GetInt32());
    }

    [TestMethod]
    public void TestURawJsonToJsonElementOnAnEmptyWindowIsUndefined()
    {
        Assert.AreEqual(JsonValueKind.Undefined, default(RawJson).ToJsonElement().ValueKind);
    }

    /// <summary>The property is found whether it comes first or after another top-level member.</summary>
    [TestMethod]
    public void TestURawJsonFindsAPropertyAtTheTopLevel()
    {
        Assert.IsTrue(Window("{\"marker\":1,\"a\":2}").HasTopLevelProperty("marker"u8));

        // Reaching it means the preceding member, itself an object holding an array, was skipped
        // whole rather than walked into.
        Assert.IsTrue(Window("{\"a\":{\"b\":[1,2]},\"marker\":1}").HasTopLevelProperty("marker"u8));
    }

    /// <summary>A property of the same name nested inside another member is not the top-level one.</summary>
    [TestMethod]
    public void TestURawJsonDoesNotMistakeANestedOccurrenceForTopLevel()
    {
        Assert.IsFalse(Window("{\"a\":[{\"marker\":1}]}").HasTopLevelProperty("marker"u8));
    }

    /// <summary>An empty object, a non-object document, and an empty window all answer false.</summary>
    [TestMethod]
    public void TestURawJsonHasNoTopLevelPropertyOnANonObjectOrEmptyInput()
    {
        Assert.IsFalse(Window("{}").HasTopLevelProperty("marker"u8));
        Assert.IsFalse(Window("[1,2]").HasTopLevelProperty("marker"u8));
        Assert.IsFalse(default(RawJson).HasTopLevelProperty("marker"u8));
    }

    /// <summary>
    /// Works only because the scan goes through ValueTextEquals, which unescapes. Swapping it for
    /// a raw byte comparison would pass every other case here and break this one silently.
    /// </summary>
    [TestMethod]
    public void TestURawJsonMatchesAnEscapedTopLevelKey()
    {
        Assert.IsTrue(Window("{\"\\u006darker\":1}").HasTopLevelProperty("marker"u8));
    }

    private static RawJson Window(string json)
    {
        byte[] frame = Encoding.UTF8.GetBytes(json);
        return new RawJson(frame, 0, frame.Length);
    }
}
