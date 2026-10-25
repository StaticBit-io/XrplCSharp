using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json.Nodes;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.Utils.Hashes;

namespace XrplTests.Xrpl.Utils
{
    /// <summary>
    /// <see cref="Hashes.HashTx"/> is the single-signing hash, as xrpl.js <c>hashTx</c> is; the
    /// transaction ID is <see cref="HashLedger.HashSignedTx(string)"/>.
    /// </summary>
    [TestClass]
    public class TestUHashTx
    {
        // xrpl.js test/fixtures/rippled/tx/offerCreateSell.json and the ID hashes.test.ts expects for it.
        private const string OfferCreateSell = """
            {
              "Account": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
              "Fee": "12",
              "Flags": 2148007936,
              "LastLedgerSequence": 105,
              "Sequence": 2,
              "SigningPubKey": "0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020",
              "TakerGets": { "currency": "USD", "issuer": "rMH4UxPrbuMa1spCBR98hLLyNJp4d8p4tM", "value": "10.1" },
              "TakerPays": "254391353000000",
              "TransactionType": "OfferCreate",
              "TxnSignature": "30440221008C13CA1BD56431B643FD145CDE7BE1805424B48FDF40E0D1A8C2FD53FAACA974021F6393721438C01B9E3138D55469049C8B72B4F6A4508ACA3C0036788C300459"
            }
            """;

        private const string OfferCreateSellId = "458101D51051230B1D56E9ACAFAA34451BF65FA000F95DF6F0FF5B3A62D83FC2";

        [TestMethod]
        public void HashSignedTx_XrplJsVector_IsTheTransactionId()
        {
            string blob = XrplBinaryCodec.Encode(JsonNode.Parse(OfferCreateSell));

            Assert.AreEqual(OfferCreateSellId, HashLedger.HashSignedTx(blob));
            Assert.AreEqual(OfferCreateSellId, HashLedger.HashSignedTx(JsonNode.Parse(OfferCreateSell)));
        }

        /// <summary>
        /// HashTx prefixes the blob with STX\0, so over the signing fields it is the digest a
        /// single signer signs (sha512Half of what <c>EncodeForSigning</c> returns, prefix included),
        /// and over the signed blob it is not the ID.
        /// </summary>
        [TestMethod]
        public void HashTx_IsTheSingleSigningHash_NotTheId()
        {
            JsonNode tx = JsonNode.Parse(OfferCreateSell);
            string preimage = XrplBinaryCodec.EncodeForSigning(tx);
            string signingFields = preimage.Substring(8);

            Assert.IsTrue(preimage.StartsWith("53545800"), "EncodeForSigning carries the STX prefix");
            Assert.AreEqual(Sha512.Half(preimage.FromHex()).ToHex(), signingFields.HashTx());
            Assert.AreNotEqual(OfferCreateSellId, XrplBinaryCodec.Encode(tx).HashTx());
        }

        [TestMethod]
        public void HashTX_IsTheSameAsHashTx()
        {
            string signingBlob = XrplBinaryCodec.EncodeForSigning(JsonNode.Parse(OfferCreateSell)).Substring(8);

#pragma warning disable CS0618 // the obsolete duplicate is what is under test
            Assert.AreEqual(signingBlob.HashTx(), signingBlob.HashTX());
#pragma warning restore CS0618
        }
    }
}
