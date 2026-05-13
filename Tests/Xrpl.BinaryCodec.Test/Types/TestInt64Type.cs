using System;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib.Types;

[TestClass]
public class TestInt64Type
{
    [TestMethod]
    public void TestFromJson_Number()
    {
        JsonNode json = JsonValue.Create(9999999999L);
        Int64Type value = Int64Type.FromJson(json);
        Assert.AreEqual(9999999999L, value.Value);
    }

    [TestMethod]
    public void TestFromJson_String()
    {
        JsonNode json = JsonValue.Create("123456789012345");
        Int64Type value = Int64Type.FromJson(json);
        Assert.AreEqual(123456789012345L, value.Value);
    }

    [TestMethod]
    public void TestFromJson_NegativeString()
    {
        JsonNode json = JsonValue.Create("-999");
        Int64Type value = Int64Type.FromJson(json);
        Assert.AreEqual(-999L, value.Value);
    }

    [TestMethod]
    public void TestFromJson_Zero()
    {
        JsonNode json = JsonValue.Create("0");
        Int64Type value = Int64Type.FromJson(json);
        Assert.AreEqual(0L, value.Value);
    }

    [TestMethod]
    public void TestFromJson_MaxValue()
    {
        JsonNode json = JsonValue.Create(long.MaxValue.ToString());
        Int64Type value = Int64Type.FromJson(json);
        Assert.AreEqual(long.MaxValue, value.Value);
    }

    [TestMethod]
    public void TestFromJson_MinValue()
    {
        JsonNode json = JsonValue.Create(long.MinValue.ToString());
        Int64Type value = Int64Type.FromJson(json);
        Assert.AreEqual(long.MinValue, value.Value);
    }

    [TestMethod]
    public void TestToBytes_BigEndian()
    {
        Int64Type value = new Int64Type(1L);
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, bytes);
    }

    [TestMethod]
    public void TestToBytes_Negative()
    {
        Int64Type value = new Int64Type(-1L);
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, bytes);
    }

    [TestMethod]
    public void TestRoundtrip()
    {
        long[] testValues = { 0L, 1L, -1L, long.MaxValue, long.MinValue, 123456789012345L, -987654321L };
        foreach (long expected in testValues)
        {
            Int64Type original = new Int64Type(expected);
            BytesList sink = new BytesList();
            original.ToBytes(sink);
            byte[] bytes = sink.ToBytes();
            string hex = BitConverter.ToString(bytes).Replace("-", "");

            BufferParser parser = new BufferParser(hex);
            Int64Type deserialized = Int64Type.FromParser(parser);
            Assert.AreEqual(expected, deserialized.Value, $"Roundtrip failed for {expected}");
        }
    }

    [TestMethod]
    public void TestToJson_ReturnsString()
    {
        Int64Type value = new Int64Type(12345L);
        JsonNode json = value.ToJson();
        Assert.AreEqual("12345", json.GetValue<string>());
    }

    [TestMethod]
    public void TestFromJson_InvalidKind_Throws()
    {
        JsonNode json = JsonValue.Create(true);
        bool threw = false;
        try { Int64Type.FromJson(json); }
        catch (FormatException) { threw = true; }
        Assert.IsTrue(threw, "Expected FormatException was not thrown.");
    }

    [TestMethod]
    public void TestImplicitConversion()
    {
        Int64Type typed = 999L;
        long raw = typed;
        Assert.AreEqual(999L, raw);
    }
}