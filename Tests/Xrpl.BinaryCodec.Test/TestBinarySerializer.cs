using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/test_binary_serializer.py

namespace Xrpl.BinaryCodec.Tests
{
    [TestClass]
    public class TestBinarySerializer
    {
        [TestMethod]
        public void TestWriteLengthEncoded_Short()
        {
            int length = 100;
            BytesList list = new BytesList();
            BinarySerializer serializer = new BinarySerializer(list);
            string byteString = "A2".Repeat(length);
            Blob blob = Blob.FromHex(byteString);
            Assert.HasCount(length, blob.Buffer);

            serializer.AddLengthEncoded(blob);

            BufferParser parser = new BufferParser(list.BytesHex());
            int decodedLength = parser.ReadVlLength();
            Assert.AreEqual(length, decodedLength);

            byte[] data = parser.Read(decodedLength);
            Assert.HasCount(length, data);
            Assert.AreEqual(0xA2, data[0]);
        }

        [TestMethod]
        public void TestWriteLengthEncoded_Medium()
        {
            int length = 300;
            BytesList list = new BytesList();
            BinarySerializer serializer = new BinarySerializer(list);
            string byteString = "B5".Repeat(length);
            Blob blob = Blob.FromHex(byteString);
            Assert.HasCount(length, blob.Buffer);

            serializer.AddLengthEncoded(blob);

            BufferParser parser = new BufferParser(list.BytesHex());
            int decodedLength = parser.ReadVlLength();
            Assert.AreEqual(length, decodedLength);
        }

        [TestMethod]
        public void TestSerializerPut()
        {
            BytesList list = new BytesList();
            BinarySerializer serializer = new BinarySerializer(list);
            serializer.Put(new byte[] { 0x01, 0x02, 0x03 });

            Assert.AreEqual("010203", list.BytesHex());
        }
    }
}
