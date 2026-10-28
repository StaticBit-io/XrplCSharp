using System;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

namespace Xrpl.BinaryCodec.Tests
{
    [TestClass]
    public class TestUMinorFixes
    {
        private const string MptId = "00000001B5E1A9A0C3A4C9F0A3A1B2C3D4E5F60718293A4B";

        private static JsonObject MptAmountJson(string value) => new JsonObject
        {
            ["mpt_issuance_id"] = MptId,
            ["value"] = value,
        };

        [TestMethod]
        public void MptAmount_Fractional_Throws()
        {
            Assert.ThrowsExactly<BinaryCodecException>(() => Amount.FromJson(MptAmountJson("1.5")));
        }

        [TestMethod]
        public void MptAmount_AboveInt64Max_Throws()
        {
            Assert.ThrowsExactly<BinaryCodecException>(() => Amount.FromJson(MptAmountJson("9223372036854775808")));
        }

        [TestMethod]
        public void MptAmount_Int64Max_RoundTrips()
        {
            Amount amount = Amount.FromJson(MptAmountJson("9223372036854775807"));
            Assert.AreEqual("9223372036854775807", amount.ToJson()["value"]!.GetValue<string>());
        }

        [TestMethod]
        public void MptAmount_NegativeInteger_Accepted()
        {
            Amount amount = Amount.FromJson(MptAmountJson("-5"));
            Assert.AreEqual("-5", amount.ToJson()["value"]!.GetValue<string>());
        }

        [TestMethod]
        public void Amount_DecimalValue_NegativeIou_Parses()
        {
            Amount amount = Amount.FromJson(new JsonObject
            {
                ["value"] = "-1.5",
                ["currency"] = "USD",
                ["issuer"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            });
            Assert.AreEqual(-1.5m, amount.DecimalValue());
        }

        [TestMethod]
        public void Uint32_FromJson_CodeBuiltValues_AreRead()
        {
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7)));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7u)));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7L)));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7UL)));
            Assert.AreEqual(uint.MaxValue, (uint)Uint32.FromJson(JsonValue.Create((long)uint.MaxValue)));
        }

        [TestMethod]
        public void Uint32_FromJson_OutOfRange_Throws()
        {
            Assert.ThrowsExactly<OverflowException>(() => Uint32.FromJson(JsonValue.Create(-1)));
            Assert.ThrowsExactly<OverflowException>(() => Uint32.FromJson(JsonValue.Create(uint.MaxValue + 1L)));
            Assert.ThrowsExactly<OverflowException>(() => Uint32.FromJson(JsonValue.Create(ulong.MaxValue)));
        }

        [TestMethod]
        public void Uint32_FromJson_ParsedJson_StillWorks()
        {
            Assert.AreEqual(4294967295u, (uint)Uint32.FromJson(JsonNode.Parse("4294967295")!));
            Assert.AreEqual(12u, (uint)Uint32.FromJson(JsonNode.Parse("\"12\"")!));
        }

        [TestMethod]
        public void Hash256_EqualsAndGetHashCode_AreValueBased()
        {
            byte[] bytes = new byte[32];
            bytes[31] = 9;
            Hash256 a = new Hash256((byte[])bytes.Clone());
            Hash256 b = new Hash256((byte[])bytes.Clone());
            Hash256 c = Hash256.Zero;

            Assert.IsTrue(a.Equals((object)b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals((object)c));
            Assert.IsFalse(a.Equals((object)"x"));
            Assert.IsFalse(a.Equals((object)null));
        }

        [TestMethod]
        public void Hash_WrongLength_ThrowsArgumentException()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new Hash128(new byte[15]));
            Assert.ThrowsExactly<ArgumentException>(() => new Hash160(new byte[21]));
            Assert.ThrowsExactly<ArgumentException>(() => new Hash256(new byte[31]));
            Assert.ThrowsExactly<ArgumentException>(() => new Hash256(null));
        }

        [TestMethod]
        public void EncodeVl_MaxLength_IsEncodable_AndOneMoreIsRejected()
        {
            byte[] max = BinarySerializer.EncodeVl(918744);
            Assert.AreEqual(3, max.Length);
            Assert.AreEqual(254, max[0]);
            Assert.ThrowsExactly<InvalidOperationException>(() => BinarySerializer.EncodeVl(918745));
        }

        [TestMethod]
        public void SerializedEnumeration_ReadOrdinal_ReadsBigEndian()
        {
            BinaryParser parser = new BufferParser("0102");
            Assert.AreEqual(0x0102, TransactionType.Values.ReadOrdinal(parser));
        }

        [TestMethod]
        public void MptAmount_FractionBeyondDecimalPrecision_Throws()
        {
            // 31 significant digits: decimal would round this to 1.
            Assert.ThrowsExactly<BinaryCodecException>(() => Amount.FromJson(MptAmountJson("1.000000000000000000000000000001")));
        }

        [TestMethod]
        public void MptAmount_ExponentForms_AreReadExactly()
        {
            Assert.AreEqual("1000", Amount.FromJson(MptAmountJson("1e3")).ToJson()["value"]!.GetValue<string>());
            Assert.AreEqual("15", Amount.FromJson(MptAmountJson("1.5e1")).ToJson()["value"]!.GetValue<string>());
            Assert.ThrowsExactly<BinaryCodecException>(() => Amount.FromJson(MptAmountJson("15e-1")));
            Assert.ThrowsExactly<BinaryCodecException>(() => Amount.FromJson(MptAmountJson("abc")));
        }

        [TestMethod]
        public void Uint32_FromJson_FloatingValues_MustBeWhole()
        {
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7d)));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7f)));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonValue.Create(7m)));
            Assert.ThrowsExactly<OverflowException>(() => Uint32.FromJson(JsonValue.Create(1.0000000000000002d)));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonNode.Parse("7.0")));
            Assert.AreEqual(7u, (uint)Uint32.FromJson(JsonNode.Parse("7e0")));
            Assert.ThrowsExactly<OverflowException>(() => Uint32.FromJson(JsonNode.Parse("7.5")));
        }
    }
}
