using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.BinaryCodec.ShaMapTree;
using Xrpl.BinaryCodec.Types;
using Xrpl.BinaryCodec.Util;

using CodecExtensions = Xrpl.BinaryCodec.ExtensionHelpers;

namespace Xrpl.BinaryCodec.Tests
{
    [TestClass]
    public class TestUBaseCodecRegressions
    {
        private const string Channel = "43904CBFCDCEC530B4037871F86EE90BF799DF8D2E0EA564BC8A3F332E4F5FB1";

        [TestMethod]
        public void EncodeForSigningClaim_AmountAboveInt32_EncodesAsUInt64()
        {
            Dictionary<string, object> claim = new Dictionary<string, object>
            {
                { "amount", "3000000000" },
                { "channel", Channel },
            };
            Assert.AreEqual("434C4D00" + Channel + "00000000B2D05E00", XrplBinaryCodec.EncodeForSigningClaim(claim));
        }

        [TestMethod]
        public void EncodeForSigningClaim_MaxUInt64_Encodes()
        {
            Dictionary<string, object> claim = new Dictionary<string, object>
            {
                { "amount", "18446744073709551615" },
                { "channel", Channel },
            };
            Assert.AreEqual("434C4D00" + Channel + "FFFFFFFFFFFFFFFF", XrplBinaryCodec.EncodeForSigningClaim(claim));
        }

        [TestMethod]
        public void EncodeForSigningClaim_SignedOrFractionalAmount_Throws()
        {
            foreach (string amount in new[] { "-1", "+1", "1.5", "1e3", " 1" })
            {
                Dictionary<string, object> claim = new Dictionary<string, object>
                {
                    { "amount", amount },
                    { "channel", Channel },
                };
                Assert.ThrowsExactly<FormatException>(() => XrplBinaryCodec.EncodeForSigningClaim(claim), amount);
            }
        }

        [TestMethod]
        public void BufferParser_ReadUIntN_ReturnsAllBytes()
        {
            BufferParser parser = new BufferParser("01020304A1B2");
            Assert.AreEqual(0x01020304u, parser.ReadUIntN(4));
            Assert.AreEqual(0xA1B2u, parser.ReadUIntN(2));
        }

        [TestMethod]
        public void StreamParser_ReadUIntN_ReturnsAllBytes()
        {
            StreamParser parser = new StreamParser(new MemoryStream("FFEEDDCC0102".FromHex()));
            Assert.AreEqual(0xFFEEDDCCu, parser.ReadUIntN(4));
            Assert.AreEqual(0x0102u, parser.ReadUIntN(2));
        }

        [TestMethod]
        public void StreamParser_Peek_DoesNotConsume()
        {
            StreamParser parser = new StreamParser(new MemoryStream("0A0B".FromHex()));
            Assert.AreEqual((byte)0x0A, parser.Peek());
            Assert.AreEqual((byte)0x0A, parser.Peek());
            Assert.AreEqual(0, parser.Pos());
            Assert.AreEqual((byte)0x0A, parser.ReadOne());
            Assert.AreEqual((byte)0x0B, parser.ReadOne());
            Assert.ThrowsExactly<InvalidOperationException>(() => parser.Peek());
        }

        [TestMethod]
        public void Amount_FromStreamParser_ReadsWholeXrpAmount()
        {
            const string drops1000 = "40000000000003E8";
            Amount amount = Amount.FromParser(new StreamParser(new MemoryStream(drops1000.FromHex())));
            Assert.AreEqual(drops1000, amount.ToHex());
        }

        [TestMethod]
        public void Exponent_AccountsForFractionAndScientificNotation()
        {
            Assert.AreEqual(-1, CodecExtensions.Exponent("1.5"));
            Assert.AreEqual(5, CodecExtensions.Exponent("1e5"));
            Assert.AreEqual(2, CodecExtensions.Exponent("100"));
            Assert.AreEqual(-2, CodecExtensions.Exponent("1.230"));
            Assert.AreEqual(-5, CodecExtensions.Exponent("12.5e-4"));
            Assert.AreEqual(0, CodecExtensions.Exponent("0.000"));
            Assert.AreEqual(-1, CodecExtensions.NumFuncAll("1.5").Exponent);
            Assert.AreEqual(0, CodecExtensions.NumFuncAll("0").Exponent);
        }

        [TestMethod]
        public void Uint16_FromValue_KeepsHighByte()
        {
            Assert.AreEqual("0102", Uint16.FromValue(0x0102).ToHex());
            Assert.AreEqual("FFFF", Uint16.FromValue(ushort.MaxValue).ToHex());
            Assert.ThrowsExactly<BinaryCodecException>(() => Uint16.FromValue(ushort.MaxValue + 1));
            Assert.ThrowsExactly<BinaryCodecException>(() => Uint16.FromValue(-1));
        }

        [TestMethod]
        public void Uint32_FromValue_KeepsHighBytes()
        {
            Assert.AreEqual("01020304", Uint32.FromValue(0x01020304).ToHex());
            Assert.AreEqual("7FFFFFFF", Uint32.FromValue(int.MaxValue).ToHex());
            Assert.ThrowsExactly<BinaryCodecException>(() => Uint32.FromValue(-1));
        }

        [TestMethod]
        public void Uint64_FromValueString_PadsOnTheLeft()
        {
            Assert.AreEqual("00000000000000FF", Uint64.FromValue("FF").ToHex());
            Assert.AreEqual("0123456789ABCDEF", Uint64.FromValue("0123456789ABCDEF").ToHex());
        }

        [TestMethod]
        public void Bits_ToString_KeepsByteOrder()
        {
            byte[] bytes = { 0x7F, 0x2C, 0x4A };
            Assert.AreEqual("7F-2C-4A", Bits.ToString(bytes));
            Assert.AreEqual("2C-4A", Bits.ToString(bytes, 1));
            Assert.AreEqual("7F-2C", Bits.ToString(bytes, 0, 2));
        }

        [TestMethod]
        public void ShaMap_CopyWithRootOnly_AddDoesNotThrowOrLeak()
        {
            ShaMap original = new ShaMap();
            Hash256 a = Index('1', '0');
            Hash256 b = Index('2', '0');
            original.AddItem(a, new BlobItem(0x01));

            ShaMap copy = original.Copy();
            Assert.IsTrue(copy.AddItem(b, new BlobItem(0x02)));

            Assert.IsTrue(copy.HasLeaf(b));
            Assert.IsFalse(original.HasLeaf(b));
        }

        [TestMethod]
        public void ShaMap_CopyWithSharedInner_MutationDoesNotReachOriginal()
        {
            // The indices share their first nibble, so they sit under one inner node below the root that both maps share.
            Hash256 a = Index('1', '0');
            Hash256 b = Index('1', '1');
            Hash256 c = Index('1', '2');
            ShaMap original = Build((a, 0x01), (b, 0x02));
            // Hash256 has reference equality, so hashes are compared as hex.
            string originalHash = original.Hash().ToString();

            ShaMap copy = original.Copy();
            Assert.IsTrue(copy.AddItem(c, new BlobItem(0x03)));
            Assert.IsTrue(copy.RemoveLeaf(a));

            Assert.IsFalse(original.HasLeaf(c));
            Assert.IsTrue(original.HasLeaf(a));
            Assert.AreEqual(originalHash, original.Hash().ToString());
            Assert.IsTrue(copy.HasLeaf(c));
            Assert.IsFalse(copy.HasLeaf(a));
            Assert.AreEqual(Build((b, 0x02), (c, 0x03)).Hash().ToString(), copy.Hash().ToString());
        }

        [TestMethod]
        public void ShaMap_MutatingOriginalAfterCopy_DoesNotReachCopy()
        {
            Hash256 a = Index('1', '0');
            Hash256 b = Index('1', '1');
            Hash256 c = Index('1', '2');
            ShaMap original = Build((a, 0x01), (b, 0x02));

            ShaMap copy = original.Copy();
            Assert.IsTrue(original.AddItem(c, new BlobItem(0x03)));

            Assert.IsFalse(copy.HasLeaf(c));
            Assert.AreEqual(Build((a, 0x01), (b, 0x02)).Hash().ToString(), copy.Hash().ToString());
            Assert.AreEqual(Build((a, 0x01), (b, 0x02), (c, 0x03)).Hash().ToString(), original.Hash().ToString());
        }

        private static ShaMap Build(params (Hash256 Index, byte Value)[] items)
        {
            ShaMap map = new ShaMap();
            foreach ((Hash256 index, byte value) in items)
            {
                map.AddItem(index, new BlobItem(value));
            }
            return map;
        }

        private static Hash256 Index(char first, char second) =>
            Hash256.FromHex(first.ToString() + second + new string('A', 62));

        private sealed class BlobItem : IShaMapItem<object>
        {
            private readonly byte _value;

            public BlobItem(byte value) => _value = value;

            public void ToBytes(IBytesSink sink) => sink.Put(_value);

            public IShaMapItem<object> Copy() => new BlobItem(_value);

            public object Value() => _value;

            public HashPrefix Prefix() => HashPrefix.TransactionId;
        }
    }
}
