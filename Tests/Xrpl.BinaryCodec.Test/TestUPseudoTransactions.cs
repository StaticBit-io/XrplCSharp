using System;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib
{
    /// <summary>
    /// Mainnet pseudo-transactions. <c>UNLModify</c> leaves its Account defaulted, which rippled serializes
    /// as a zero-length VL (<c>81 00</c>) and renders as <c>""</c>; <c>EnableAmendment</c> and <c>SetFee</c>
    /// set the zero account, serialized as 20 zero bytes (<c>81 14 00..00</c>).
    /// </summary>
    [TestClass]
    public class TestUPseudoTransactions
    {
        // Mainnet ledger 107677184, tx BB53553FD4138C95873B7EF10421F81CBEAFBFBB4FFFA62D0A6DF81ADC744632.
        private const string UnlModifyBlob =
            "120066240000000026066B06006840000000000000007300701321ED63CF929BE85B266A66584B3FE2EB97FC248203F0271DC9C833563E60418E7818810000101100";

        // Mainnet ledger 107540993, tx 1717BD8BE3FED93C142C8CD38DA5AD1208A0E9AE4E24F0F7ED545FC750BCA20A.
        private const string EnableAmendmentBlob =
            "1200642400000000260668F20150139F287AED3CDB50A7BD1ACEC24296A30C9B5230CCD136219317AC790E3B884377684000000000000000730081140000000000000000000000000000000000000000";

        // Mainnet ledger 92508417, tx 4EEDB01BB943CE32E97BB468AC179ABF933B272D6FF990E76B6721FB48E069FC.
        private const string SetFeeBlob =
            "120065240000000026058391016840000000000000006016400000000000000A601740000000000F424060184000000000030D40730081140000000000000000000000000000000000000000";

        private const string AccountZero = "rrrrrrrrrrrrrrrrrrrrrhoLvTp";

        [TestMethod]
        public void TestUDecodeUnlModify()
        {
            JsonNode decoded = XrplBinaryCodec.Decode(UnlModifyBlob);

            Assert.AreEqual("UNLModify", decoded["TransactionType"].GetValue<string>());
            Assert.AreEqual("", decoded["Account"].GetValue<string>());
            Assert.AreEqual("0", decoded["Fee"].GetValue<string>());
            Assert.AreEqual(107677184u, decoded["LedgerSequence"].GetValue<uint>());
            Assert.AreEqual(0u, decoded["Sequence"].GetValue<uint>());
            Assert.AreEqual("", decoded["SigningPubKey"].GetValue<string>());
            Assert.AreEqual(0u, decoded["UNLModifyDisabling"].GetValue<uint>());
            Assert.AreEqual(
                "ED63CF929BE85B266A66584B3FE2EB97FC248203F0271DC9C833563E60418E7818",
                decoded["UNLModifyValidator"].GetValue<string>());
        }

        [TestMethod]
        public void TestURoundTripUnlModify()
        {
            JsonNode decoded = XrplBinaryCodec.Decode(UnlModifyBlob);

            Assert.AreEqual(UnlModifyBlob, XrplBinaryCodec.Encode(decoded));
        }

        [TestMethod]
        public void TestUEncodeRippledJsonUnlModify()
        {
            // The JSON rippled returns for the same transaction (ledger, expand: true, non-binary).
            JsonObject json = new JsonObject
            {
                ["Account"] = "",
                ["Fee"] = "0",
                ["LedgerSequence"] = 107677184u,
                ["Sequence"] = 0u,
                ["SigningPubKey"] = "",
                ["TransactionType"] = "UNLModify",
                ["UNLModifyDisabling"] = 0,
                ["UNLModifyValidator"] = "ED63CF929BE85B266A66584B3FE2EB97FC248203F0271DC9C833563E60418E7818",
            };

            Assert.AreEqual(UnlModifyBlob, XrplBinaryCodec.Encode(json));
        }

        [TestMethod]
        public void TestUDecodeAndRoundTripEnableAmendment()
        {
            JsonNode decoded = XrplBinaryCodec.Decode(EnableAmendmentBlob);

            Assert.AreEqual("EnableAmendment", decoded["TransactionType"].GetValue<string>());
            Assert.AreEqual(AccountZero, decoded["Account"].GetValue<string>());
            Assert.AreEqual(107540993u, decoded["LedgerSequence"].GetValue<uint>());
            Assert.AreEqual(
                "9F287AED3CDB50A7BD1ACEC24296A30C9B5230CCD136219317AC790E3B884377",
                decoded["Amendment"].GetValue<string>());
            Assert.AreEqual(EnableAmendmentBlob, XrplBinaryCodec.Encode(decoded));
        }

        [TestMethod]
        public void TestUDecodeAndRoundTripSetFee()
        {
            JsonNode decoded = XrplBinaryCodec.Decode(SetFeeBlob);

            Assert.AreEqual("SetFee", decoded["TransactionType"].GetValue<string>());
            Assert.AreEqual(AccountZero, decoded["Account"].GetValue<string>());
            Assert.AreEqual(92508417u, decoded["LedgerSequence"].GetValue<uint>());
            Assert.AreEqual("10", decoded["BaseFeeDrops"].GetValue<string>());
            Assert.AreEqual("1000000", decoded["ReserveBaseDrops"].GetValue<string>());
            Assert.AreEqual("200000", decoded["ReserveIncrementDrops"].GetValue<string>());
            Assert.AreEqual(SetFeeBlob, XrplBinaryCodec.Encode(decoded));
        }

        [TestMethod]
        public void TestUEmptyAccountFromJson()
        {
            AccountId account = AccountId.FromJson(JsonValue.Create(""));

            Assert.IsTrue(account.IsEmpty);
            Assert.AreEqual("", account.ToString());
            Assert.AreEqual(0, account.Buffer.Length);
        }

        [TestMethod]
        public void TestUFromParserRejectsInvalidLength()
        {
            BinaryParser parser = new BufferParser(new byte[19]);

            Assert.ThrowsExactly<FormatException>(() => AccountId.FromParser(parser, 19));
        }

        [TestMethod]
        public void TestUFromParserWithoutHintReadsTwentyBytes()
        {
            BinaryParser parser = new BufferParser("5E7B112523F68D2F5E879DB4EAC51C6698A69304");

            AccountId account = AccountId.FromParser(parser);

            Assert.IsFalse(account.IsEmpty);
            Assert.AreEqual("r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", account.ToString());
        }
    }
}
