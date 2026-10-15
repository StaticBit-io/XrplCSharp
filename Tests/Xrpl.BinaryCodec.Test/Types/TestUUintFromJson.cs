using System;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib.Types
{
    /// <summary>
    /// <see cref="Uint8.FromJson"/> and <see cref="Uint16.FromJson"/> read a node built in memory from any
    /// CLR numeric type, including the <c>uint</c> the decoder emits, so decoded JSON encodes back.
    /// </summary>
    [TestClass]
    public class TestUUintFromJson
    {
        [TestMethod]
        public void TestUUint8FromInMemoryNumbers()
        {
            Assert.AreEqual((byte)7, Uint8.FromJson(JsonValue.Create(7u)).Value);
            Assert.AreEqual((byte)7, Uint8.FromJson(JsonValue.Create(7)).Value);
            Assert.AreEqual((byte)255, Uint8.FromJson(JsonValue.Create((byte)255)).Value);
        }

        [TestMethod]
        public void TestUUint8FromParsedText()
        {
            Assert.AreEqual((byte)1, Uint8.FromJson(JsonNode.Parse("1")).Value);
            Assert.AreEqual((byte)1, Uint8.FromJson(JsonNode.Parse("\"1\"")).Value);
        }

        [TestMethod]
        public void TestUUint8OutOfRange()
        {
            Assert.ThrowsExactly<OverflowException>(() => Uint8.FromJson(JsonValue.Create(256)));
            Assert.ThrowsExactly<OverflowException>(() => Uint8.FromJson(JsonValue.Create(-1)));
            Assert.ThrowsExactly<OverflowException>(() => Uint8.FromJson(JsonNode.Parse("1.5")));
        }

        [TestMethod]
        public void TestUUint16FromInMemoryNumbers()
        {
            Assert.AreEqual((ushort)50000, Uint16.FromJson(JsonValue.Create(50000u)).Value);
            Assert.AreEqual((ushort)50000, Uint16.FromJson(JsonValue.Create(50000)).Value);
        }

        [TestMethod]
        public void TestUUint16OutOfRange()
        {
            Assert.ThrowsExactly<OverflowException>(() => Uint16.FromJson(JsonValue.Create(65536)));
        }
    }
}
