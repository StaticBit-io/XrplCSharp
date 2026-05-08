using System;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib.Types;

[TestClass]
public class TestNumberType
{
    [TestMethod]
    public void TestFromJson_NumericValue()
    {
        JsonNode json = JsonValue.Create(12345UL);
        NumberType value = NumberType.FromJson(json);
        Assert.AreEqual(12345UL, value.RawValue);
    }

    [TestMethod]
    public void TestFromJson_StringValue()
    {
        JsonNode json = JsonValue.Create("18446744073709551615");
        NumberType value = NumberType.FromJson(json);
        Assert.AreEqual(ulong.MaxValue, value.RawValue);
    }

    [TestMethod]
    public void TestFromJson_Zero()
    {
        JsonNode json = JsonValue.Create("0");
        NumberType value = NumberType.FromJson(json);
        Assert.AreEqual(0UL, value.RawValue);
    }

    [TestMethod]
    public void TestToBytes_BigEndian()
    {
        NumberType value = new NumberType(0x0102030405060708UL);
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, bytes);
    }

    [TestMethod]
    public void TestToBytes_Zero()
    {
        NumberType value = new NumberType(0UL);
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, bytes);
    }

    [TestMethod]
    public void TestRoundtrip()
    {
        ulong[] testValues = { 0UL, 1UL, ulong.MaxValue, 0xDEADBEEFCAFEBABEUL, 999999999999UL };
        foreach (ulong expected in testValues)
        {
            NumberType original = new NumberType(expected);
            BytesList sink = new BytesList();
            original.ToBytes(sink);
            byte[] bytes = sink.ToBytes();
            string hex = BitConverter.ToString(bytes).Replace("-", "");

            BufferParser parser = new BufferParser(hex);
            NumberType deserialized = NumberType.FromParser(parser);
            Assert.AreEqual(expected, deserialized.RawValue, $"Roundtrip failed for {expected}");
        }
    }

    [TestMethod]
    public void TestToJson_ReturnsString()
    {
        NumberType value = new NumberType(42UL);
        JsonNode json = value.ToJson();
        Assert.AreEqual("42", json.GetValue<string>());
    }

    [TestMethod]
    public void TestFromJson_InvalidKind_Throws()
    {
        JsonNode json = JsonValue.Create(true);
        bool threw = false;
        try { NumberType.FromJson(json); }
        catch (FormatException) { threw = true; }
        Assert.IsTrue(threw, "Expected FormatException was not thrown.");
    }
}