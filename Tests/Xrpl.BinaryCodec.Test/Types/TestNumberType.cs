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
        // "100" → mantissa=1000000000000000000 (10^18), exponent=-16
        JsonNode json = JsonValue.Create(100);
        NumberType value = NumberType.FromJson(json);
        Assert.AreEqual(1_000_000_000_000_000_000L, value.Mantissa);
        Assert.AreEqual(-16, value.Exponent);
    }

    [TestMethod]
    public void TestFromJson_StringValue()
    {
        // "10000000000000" (10^13) → mantissa=1000000000000000000 (10^18), exponent=-5
        JsonNode json = JsonValue.Create("10000000000000");
        NumberType value = NumberType.FromJson(json);
        Assert.AreEqual(1_000_000_000_000_000_000L, value.Mantissa);
        Assert.AreEqual(-5, value.Exponent);
    }

    [TestMethod]
    public void TestFromJson_Zero()
    {
        JsonNode json = JsonValue.Create("0");
        NumberType value = NumberType.FromJson(json);
        Assert.AreEqual(0L, value.Mantissa);
        Assert.AreEqual(int.MinValue, value.Exponent);
    }

    [TestMethod]
    public void TestToBytes_TwelveBytesFormat()
    {
        // Verified against rippled sign API output for PrincipalRequested="10000000000000"
        // mantissa=1000000000000000000 (10^18), exponent=-5
        // mantissa hex: 0x0DE0B6B3A7640000
        // exponent hex: 0xFFFFFFFB (-5)
        NumberType value = new NumberType(1_000_000_000_000_000_000L, -5);
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        Assert.AreEqual(12, bytes.Length, "Number type must serialize to 12 bytes");

        // Verify mantissa bytes (big-endian int64)
        byte[] expectedMantissa = { 0x0D, 0xE0, 0xB6, 0xB3, 0xA7, 0x64, 0x00, 0x00 };
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(expectedMantissa[i], bytes[i], $"Mantissa byte {i} mismatch");

        // Verify exponent bytes (big-endian int32)
        byte[] expectedExponent = { 0xFF, 0xFF, 0xFF, 0xFB };
        for (int i = 0; i < 4; i++)
            Assert.AreEqual(expectedExponent[i], bytes[8 + i], $"Exponent byte {i} mismatch");
    }

    [TestMethod]
    public void TestToBytes_MatchesRippled()
    {
        // Compare our encoding of PrincipalRequested="10000000000000" with rippled's output
        // rippled tx_blob contains: 0DE0B6B3A7640000FFFFFFFB for the Number field
        NumberType value = NumberType.FromString("10000000000000");
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();

        byte[] expected = { 0x0D, 0xE0, 0xB6, 0xB3, 0xA7, 0x64, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFB };
        Assert.AreEqual(expected.Length, bytes.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], bytes[i], $"Byte {i} mismatch");
    }

    [TestMethod]
    public void TestToBytes_Zero()
    {
        NumberType value = new NumberType(0, int.MinValue);
        BytesList sink = new BytesList();
        value.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        Assert.AreEqual(12, bytes.Length, "Zero Number must also be 12 bytes");

        // mantissa = 0 → 8 zero bytes
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(0x00, bytes[i], $"Zero mantissa byte {i} should be 0");

        // exponent = Int32.MinValue = 0x80000000
        Assert.AreEqual(0x80, bytes[8]);
        Assert.AreEqual(0x00, bytes[9]);
        Assert.AreEqual(0x00, bytes[10]);
        Assert.AreEqual(0x00, bytes[11]);
    }

    [TestMethod]
    public void TestRoundtrip()
    {
        string[] testValues = { "0", "1", "100", "10000000000000", "-500", "999999999999999" };
        foreach (string input in testValues)
        {
            NumberType original = NumberType.FromString(input);
            BytesList sink = new BytesList();
            original.ToBytes(sink);
            byte[] bytes = sink.ToBytes();
            string hex = BitConverter.ToString(bytes).Replace("-", "");

            BufferParser parser = new BufferParser(hex);
            NumberType deserialized = NumberType.FromParser(parser);
            Assert.AreEqual(original.Mantissa, deserialized.Mantissa, $"Mantissa roundtrip failed for {input}");
            Assert.AreEqual(original.Exponent, deserialized.Exponent, $"Exponent roundtrip failed for {input}");
        }
    }

    [TestMethod]
    public void TestToJson_ReturnsString()
    {
        NumberType value = NumberType.FromString("100");
        JsonNode json = value.ToJson();
        Assert.AreEqual("100", json.GetValue<string>());
    }

    [TestMethod]
    public void TestToJson_PrincipalRequested()
    {
        // Matches rippled's JSON output: "PrincipalRequested": "1e13"
        // Our ToJson should return "10000000000000" (the decimal representation)
        NumberType value = NumberType.FromString("10000000000000");
        JsonNode json = value.ToJson();
        Assert.AreEqual("10000000000000", json.GetValue<string>());
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

    [TestMethod]
    public void TestNegativeValue()
    {
        NumberType value = NumberType.FromString("-42");
        Assert.IsTrue(value.Mantissa < 0, "Mantissa should be negative for negative values");
        JsonNode json = value.ToJson();
        Assert.AreEqual("-42", json.GetValue<string>());
    }

    [TestMethod]
    public void TestMantissaNormalization()
    {
        // All non-zero values must have mantissa in [10^18, long.MaxValue] range
        NumberType value = NumberType.FromString("1");
        long absMantissa = Math.Abs(value.Mantissa);
        Assert.IsTrue(absMantissa >= 1_000_000_000_000_000_000L, "Mantissa too small (must be >= 10^18)");
        Assert.IsTrue(absMantissa <= long.MaxValue, "Mantissa too large");
    }
}
