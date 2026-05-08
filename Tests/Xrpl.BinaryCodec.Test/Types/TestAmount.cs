using System;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/types/test_amount.py

namespace XrplTests.BinaryCodecLib.Types
{
    [TestClass]
    public class TestAmount
    {
        [TestMethod]
        public void TestXrpFromJsonString()
        {
            JsonNode json = JsonValue.Create("1000");
            Amount amount = Amount.FromJson(json);
            Assert.IsTrue(amount.IsNative());
        }

        [TestMethod]
        public void TestXrpRoundtrip()
        {
            string[] xrpValues = { "0", "1", "1000000", "100000000000" };
            foreach (string val in xrpValues)
            {
                JsonNode json = JsonValue.Create(val);
                Amount original = Amount.FromJson(json);
                Assert.IsTrue(original.IsNative());

                BytesList sink = new BytesList();
                original.ToBytes(sink);
                string hex = sink.BytesHex();

                BufferParser parser = new BufferParser(hex);
                Amount deserialized = Amount.FromParser(parser);
                Assert.IsTrue(deserialized.IsNative());
                Assert.AreEqual(val, deserialized.ToJson().GetValue<string>());
            }
        }

        [TestMethod]
        public void TestXrpKnownEncoding()
        {
            // "100" XRP drops = 0x4000000000000064
            JsonNode json = JsonValue.Create("100");
            Amount amount = Amount.FromJson(json);
            BytesList sink = new BytesList();
            amount.ToBytes(sink);
            Assert.AreEqual("4000000000000064", sink.BytesHex());
        }

        [TestMethod]
        public void TestIouFromJson()
        {
            JsonObject json = new JsonObject
            {
                ["value"] = "1",
                ["currency"] = "USD",
                ["issuer"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"
            };
            Amount amount = Amount.FromJson(json);
            Assert.IsFalse(amount.IsNative());
        }

        [TestMethod]
        public void TestIouRoundtrip()
        {
            JsonObject json = new JsonObject
            {
                ["value"] = "4.2",
                ["currency"] = "CNY",
                ["issuer"] = "rKiCet8SdvWxPXnAgYarFUXMh1zCPz432Y"
            };
            Amount original = Amount.FromJson(json);
            Assert.IsFalse(original.IsNative());

            BytesList sink = new BytesList();
            original.ToBytes(sink);
            string hex = sink.BytesHex();

            // IOU is always 48 bytes = 96 hex chars
            Assert.AreEqual(96, hex.Length);

            BufferParser parser = new BufferParser(hex);
            Amount deserialized = Amount.FromParser(parser);
            Assert.IsFalse(deserialized.IsNative());

            JsonNode resultJson = deserialized.ToJson();
            Assert.AreEqual("4.2", resultJson["value"].GetValue<string>());
            Assert.AreEqual("CNY", resultJson["currency"].GetValue<string>());
            Assert.AreEqual("rKiCet8SdvWxPXnAgYarFUXMh1zCPz432Y", resultJson["issuer"].GetValue<string>());
        }

        [TestMethod]
        public void TestZeroIou()
        {
            JsonObject json = new JsonObject
            {
                ["value"] = "0",
                ["currency"] = "USD",
                ["issuer"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"
            };
            Amount amount = Amount.FromJson(json);
            Assert.IsFalse(amount.IsNative());

            BytesList sink = new BytesList();
            amount.ToBytes(sink);
            string hex = sink.BytesHex();

            // Zero IOU first byte should be 0x80
            Assert.StartsWith("80", hex);
        }

        [TestMethod]
        public void TestNegativeIou()
        {
            JsonObject json = new JsonObject
            {
                ["value"] = "-1",
                ["currency"] = "USD",
                ["issuer"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"
            };
            Amount amount = Amount.FromJson(json);
            Assert.IsFalse(amount.IsNative());

            BytesList sink = new BytesList();
            amount.ToBytes(sink);
            string hex = sink.BytesHex();

            BufferParser parser = new BufferParser(hex);
            Amount deserialized = Amount.FromParser(parser);
            JsonNode resultJson = deserialized.ToJson();
            Assert.AreEqual("-1", resultJson["value"].GetValue<string>());
        }

        [TestMethod]
        public void TestFromJson_NullThrows()
        {
            bool threw = false;
            try { Amount.FromJson(null); }
            catch (InvalidJsonException) { threw = true; }
            Assert.IsTrue(threw, "Expected InvalidJsonException for null JSON.");
        }

        [TestMethod]
        public void TestFromJson_NumericDrops()
        {
            JsonNode json = JsonValue.Create(1000000UL);
            Amount amount = Amount.FromJson(json);
            Assert.IsTrue(amount.IsNative());
            Assert.AreEqual("1000000", amount.ToJson().GetValue<string>());
        }
    }
}
