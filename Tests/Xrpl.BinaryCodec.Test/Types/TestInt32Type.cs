using System;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib.Types
{
    [TestClass]
    public class TestInt32Type
    {
        [TestMethod]
        public void TestFromJson_Positive()
        {
            JsonNode json = JsonValue.Create(42);
            Int32Type value = Int32Type.FromJson(json);
            Assert.AreEqual(42, value.Value);
        }

        [TestMethod]
        public void TestFromJson_Negative()
        {
            JsonNode json = JsonValue.Create(-100);
            Int32Type value = Int32Type.FromJson(json);
            Assert.AreEqual(-100, value.Value);
        }

        [TestMethod]
        public void TestFromJson_Zero()
        {
            JsonNode json = JsonValue.Create(0);
            Int32Type value = Int32Type.FromJson(json);
            Assert.AreEqual(0, value.Value);
        }

        [TestMethod]
        public void TestFromJson_MaxValue()
        {
            JsonNode json = JsonValue.Create(int.MaxValue);
            Int32Type value = Int32Type.FromJson(json);
            Assert.AreEqual(int.MaxValue, value.Value);
        }

        [TestMethod]
        public void TestFromJson_MinValue()
        {
            JsonNode json = JsonValue.Create(int.MinValue);
            Int32Type value = Int32Type.FromJson(json);
            Assert.AreEqual(int.MinValue, value.Value);
        }

        [TestMethod]
        public void TestToBytes_BigEndian()
        {
            Int32Type value = new Int32Type(1);
            BytesList sink = new BytesList();
            value.ToBytes(sink);
            byte[] bytes = sink.ToBytes();
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x00, 0x01 }, bytes);
        }

        [TestMethod]
        public void TestToBytes_Negative()
        {
            Int32Type value = new Int32Type(-1);
            BytesList sink = new BytesList();
            value.ToBytes(sink);
            byte[] bytes = sink.ToBytes();
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, bytes);
        }

        [TestMethod]
        public void TestRoundtrip()
        {
            int[] testValues = { 0, 1, -1, 42, -42, int.MaxValue, int.MinValue, 123456789 };
            foreach (int expected in testValues)
            {
                Int32Type original = new Int32Type(expected);
                BytesList sink = new BytesList();
                original.ToBytes(sink);
                byte[] bytes = sink.ToBytes();
                string hex = BitConverter.ToString(bytes).Replace("-", "");

                BufferParser parser = new BufferParser(hex);
                Int32Type deserialized = Int32Type.FromParser(parser);
                Assert.AreEqual(expected, deserialized.Value, $"Roundtrip failed for {expected}");
            }
        }

        [TestMethod]
        public void TestToJson()
        {
            Int32Type value = new Int32Type(42);
            JsonNode json = value.ToJson();
            Assert.AreEqual(42, json.GetValue<int>());
        }

        [TestMethod]
        public void TestImplicitConversion()
        {
            Int32Type typed = 100;
            int raw = typed;
            Assert.AreEqual(100, raw);
        }
    }
}
