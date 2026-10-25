// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/hashLedger.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.Utils
{
    [TestClass]
    public class TestUHashLedger
    {
        private const string Account = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

        // Parsed rather than built from CLR values: the codec reads numbers from parsed JSON.
        private static JsonObject AccountSet(uint flags = 0) => JsonNode.Parse(
            $"{{\"TransactionType\":\"AccountSet\",\"Account\":\"{Account}\",\"Fee\":\"12\",\"Sequence\":1,\"Flags\":{flags}}}").AsObject();

        [TestMethod]
        public void HashSignedTx_UnsignedBlob_Throws()
        {
            string blob = XrplBinaryCodec.Encode(AccountSet());

            ValidationException exception = Assert.ThrowsExactly<ValidationException>(() => global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(blob));
            StringAssert.Contains(exception.Message, "must be signed");
        }

        [TestMethod]
        public void HashSignedTx_UnsignedJson_Throws()
        {
            Assert.ThrowsExactly<ValidationException>(() => global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(AccountSet()));
        }

        [TestMethod]
        public void HashSignedTx_SignedBlob_ReturnsTheWalletsHash()
        {
            XrplWallet wallet = XrplWallet.FromSeed("snGHNrPbHrdUcszeuDEigMdC1Lyyd");
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                ["TransactionType"] = "AccountSet",
                ["Account"] = wallet.ClassicAddress,
                ["Fee"] = "12",
                ["Sequence"] = 1,
                ["Flags"] = 0,
            };

            SignatureResult signed = wallet.Sign(tx);

            Assert.AreEqual(64, signed.Hash.Length);
            Assert.AreEqual(signed.Hash, global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(signed.TxBlob));
            Assert.AreEqual(signed.Hash, global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(XrplBinaryCodec.Decode(signed.TxBlob)));
        }

        /// <summary>
        /// A blob whose only signature is a co-signer's, as the wallet hashes it while it travels
        /// between signers, and an inner Batch transaction, which is never signed on its own.
        /// </summary>
        [TestMethod]
        public void HashSignedTx_PartiallySignedOrInnerBatch_IsHashed()
        {
            JsonObject withPubKey = AccountSet();
            withPubKey["SigningPubKey"] = "";
            Assert.AreEqual(64, global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(XrplBinaryCodec.Encode(withPubKey)).Length);

            JsonObject inner = AccountSet(flags: 0x40000000u);
            Assert.AreEqual(64, global::Xrpl.Utils.Hashes.HashLedger.HashSignedTx(XrplBinaryCodec.Encode(inner)).Length);
        }
    }
}
