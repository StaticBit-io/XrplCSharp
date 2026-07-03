using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;

namespace XrplTests.BinaryCodecLib
{
    /// <summary>
    /// Tests for the BatchV1_1 (XLS-56) signing preimage layout.
    /// Expected layout mirrors rippled serializeBatch():
    /// "BCH\0" || outerAccount(20) || outerSequence(4) || Flags(4) || Count(4) || txID[i](32 each).
    /// </summary>
    [TestClass]
    public class TestUSigningBatch
    {
        private const string OuterAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
        private const string OuterAccountIdHex = "B5F762798A53D543A014CAF8B297CFF8F2F937E8";

        private const string TxId1 = "1D5A924A24F9AF5F1BF2AD9F4E385E4C0F5B85B94E4C4A1D8B6E1C0B7F3D2E1A";
        private const string TxId2 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");

        [TestMethod]
        public void TestUEncodeForSigningBatch_KnownLayout()
        {
            const uint sequence = 7;
            const uint flags = 0x00010000; // tfAllOrNothing

            byte[] preimage = XrplBinaryCodec.EncodeForSigningBatch(OuterAccount, sequence, flags, new[] { TxId1, TxId2 });

            string expected =
                "42434800" +            // HashPrefix.Batch "BCH\0"
                OuterAccountIdHex +     // outer Account (20 bytes)
                "00000007" +            // outer Sequence
                "00010000" +            // Flags
                "00000002" +            // txID count
                TxId1 +
                TxId2;

            Assert.AreEqual(expected, ToHex(preimage));
        }

        [TestMethod]
        public void TestUEncodeForSigningBatch_TicketSequenceZero()
        {
            byte[] preimage = XrplBinaryCodec.EncodeForSigningBatch(OuterAccount, 0, 0x00080000, new[] { TxId1 });

            string expected =
                "42434800" +
                OuterAccountIdHex +
                "00000000" +            // Sequence = 0 when using TicketSequence
                "00080000" +            // tfIndependent
                "00000001" +
                TxId1;

            Assert.AreEqual(expected, ToHex(preimage));
        }

        [TestMethod]
        public void TestUEncodeForSigningBatch_NullAccount_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                XrplBinaryCodec.EncodeForSigningBatch(null, 1, 0, new[] { TxId1 }));
        }

        [TestMethod]
        public void TestUEncodeForSigningBatch_NullTxIds_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() =>
                XrplBinaryCodec.EncodeForSigningBatch(OuterAccount, 1, 0, null));
        }
    }
}
