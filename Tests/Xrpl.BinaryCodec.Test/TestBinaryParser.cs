using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/test_binary_parser.py

namespace Xrpl.BinaryCodec.Tests
{
    [TestClass]
    public class TestUBinaryParser
    {
        [TestMethod]
        public void TestPeekSkipReadMethods()
        {
            string test_hex = "00112233445566";
            byte[] testBytes = test_hex.FromHex();
            BufferParser binaryParser = new BufferParser(test_hex);

            byte firstByte = binaryParser.Peek();
            Assert.AreEqual(testBytes[0], firstByte);

            binaryParser.Skip(3);
            Assert.AreEqual(3, binaryParser.Pos());

            byte[] nextTwoBytes = binaryParser.Read(2);
            Assert.AreEqual(testBytes[3], nextTwoBytes[0]);
            Assert.AreEqual(testBytes[4], nextTwoBytes[1]);
        }

        [TestMethod]
        public void TestReadUInt8()
        {
            string test_hex = "01FF7F";
            BufferParser parser = new BufferParser(test_hex);

            byte val1 = parser.ReadUInt8();
            Assert.AreEqual((byte)1, val1);

            byte val2 = parser.ReadUInt8();
            Assert.AreEqual((byte)255, val2);

            byte val3 = parser.ReadUInt8();
            Assert.AreEqual((byte)127, val3);
        }

        [TestMethod]
        public void TestReadVlLength_Short()
        {
            // VL length <= 192: single byte encodes the length directly
            BytesList list = new BytesList();
            BinarySerializer serializer = new BinarySerializer(list);
            string byteString = "A2".Repeat(100);
            Blob blob = Blob.FromHex(byteString);
            Assert.AreEqual(100, blob.Buffer.Length);

            serializer.AddLengthEncoded(blob);
            string encoded = list.BytesHex();

            BufferParser parser = new BufferParser(encoded);
            int decodedLength = parser.ReadVlLength();
            Assert.AreEqual(100, decodedLength);
        }

        [TestMethod]
        public void TestReadVlLength_Medium()
        {
            // VL length 193..12480: two bytes
            BytesList list = new BytesList();
            BinarySerializer serializer = new BinarySerializer(list);
            string byteString = "B3".Repeat(250);
            Blob blob = Blob.FromHex(byteString);
            Assert.AreEqual(250, blob.Buffer.Length);

            serializer.AddLengthEncoded(blob);
            string encoded = list.BytesHex();

            BufferParser parser = new BufferParser(encoded);
            int decodedLength = parser.ReadVlLength();
            Assert.AreEqual(250, decodedLength);
        }

        [TestMethod]
        public void TestReadVlLength_Various()
        {
            int[] cases = { 1, 50, 100, 192, 193, 500, 1000 };
            foreach (int length in cases)
            {
                BytesList list = new BytesList();
                BinarySerializer serializer = new BinarySerializer(list);
                string byteString = "AA".Repeat(length);
                Blob blob = Blob.FromHex(byteString);

                serializer.AddLengthEncoded(blob);
                string encoded = list.BytesHex();

                BufferParser parser = new BufferParser(encoded);
                int decodedLength = parser.ReadVlLength();
                Assert.AreEqual(length, decodedLength, $"VL decode failed for length {length}");
            }
        }

        [TestMethod]
        public void TestEndAndPos()
        {
            string test_hex = "AABBCCDD";
            BufferParser parser = new BufferParser(test_hex);

            Assert.AreEqual(0, parser.Pos());
            Assert.IsFalse(parser.End());

            parser.Read(4);
            Assert.AreEqual(4, parser.Pos());
            Assert.IsTrue(parser.End());
        }
    }
}
