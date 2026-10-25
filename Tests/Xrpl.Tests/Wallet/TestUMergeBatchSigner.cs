using System;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Wallet;

namespace XrplTests.Xrpl.Wallet
{
    /// <summary>
    /// MergeBatchSigner with single-signed BatchSigners: the signature reaches the target, and two
    /// different signatures for one account are refused rather than one being dropped.
    /// </summary>
    [TestClass]
    public class TestUMergeBatchSigner
    {
        private const string Account = "rJcvzBFCTcCrQdTLPZoRXJEboT9C3Wrd6H";
        private const string PubKey = "0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020";

        private static JsonObject Signed(string signature) => new JsonObject
        {
            ["Account"] = Account,
            ["SigningPubKey"] = PubKey,
            ["TxnSignature"] = signature,
        };

        [TestMethod]
        public void TestUMergeBatchSigner_UnsignedTarget_TakesTheIncomingSignature()
        {
            JsonObject target = new JsonObject { ["Account"] = Account };

            BatchSigningHelper.MergeBatchSigner(target, Signed("AABB"));

            Assert.AreEqual(PubKey, target["SigningPubKey"]?.GetValue<string>());
            Assert.AreEqual("AABB", target["TxnSignature"]?.GetValue<string>());
        }

        [TestMethod]
        public void TestUMergeBatchSigner_SameSignature_IsANoOp()
        {
            JsonObject target = Signed("AABB");

            BatchSigningHelper.MergeBatchSigner(target, Signed("AABB"));

            Assert.AreEqual("AABB", target["TxnSignature"]?.GetValue<string>());
        }

        [TestMethod]
        public void TestUMergeBatchSigner_ConflictingSignatures_Throw()
        {
            JsonObject target = Signed("AABB");

            Helper.ThrowsException<InvalidOperationException>(
                () => BatchSigningHelper.MergeBatchSigner(target, Signed("CCDD")));
            Assert.AreEqual("AABB", target["TxnSignature"]?.GetValue<string>());
        }

        [TestMethod]
        public void TestUMergeBatchSigner_SingleSignatureForAMultiSignedTarget_Throws()
        {
            JsonObject target = new JsonObject
            {
                ["Account"] = Account,
                ["Signers"] = new JsonArray(new JsonObject
                {
                    ["Signer"] = new JsonObject { ["Account"] = Account, ["SigningPubKey"] = PubKey, ["TxnSignature"] = "EEFF" },
                }),
            };

            Helper.ThrowsException<InvalidOperationException>(
                () => BatchSigningHelper.MergeBatchSigner(target, Signed("AABB")));
        }
    }
}
