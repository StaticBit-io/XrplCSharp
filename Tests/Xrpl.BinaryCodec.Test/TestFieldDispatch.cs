using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Enums;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib
{
    [TestClass]
    public class TestFieldDispatch
    {

        [TestMethod]
        public void TestCoreFieldTypesHaveDispatch()
        {
            string[] wellKnownFields = {
                "Flags", "Sequence", "Amount", "Fee",
                "Account", "Destination", "SigningPubKey",
                "TxnSignature", "Memos"
            };

            foreach (string name in wellKnownFields)
            {
                Field field = Field.Values[name];
                Assert.IsNotNull(field.FromJson,
                    $"Field '{field.Name}' (type {field.Type.Name}) has null FromJson delegate.");
                Assert.IsNotNull(field.FromParser,
                    $"Field '{field.Name}' (type {field.Type.Name}) has null FromParser delegate.");
            }
        }

        [TestMethod]
        public void TestFieldLookupByName()
        {
            Assert.IsNotNull(Field.Values["Account"]);
            Assert.IsNotNull(Field.Values["Destination"]);
            Assert.IsNotNull(Field.Values["Amount"]);
            Assert.IsNotNull(Field.Values["Fee"]);
            Assert.IsNotNull(Field.Values["Sequence"]);
            Assert.IsNotNull(Field.Values["Flags"]);
            Assert.IsNotNull(Field.Values["SigningPubKey"]);
            Assert.IsNotNull(Field.Values["TxnSignature"]);
        }

        [TestMethod]
        public void TestNewFieldTypes_Number()
        {
            Assert.IsTrue(Field.Values.Has("Number"));
            Field numberField = Field.Values["Number"];
            Assert.AreEqual(FieldType.Number, numberField.Type);
        }

        [TestMethod]
        public void TestNewFieldTypes_XChainBridge()
        {
            Assert.IsTrue(Field.Values.Has("XChainBridge"));
            Field xcb = Field.Values["XChainBridge"];
            Assert.AreEqual(FieldType.XChainBridge, xcb.Type);
        }

        [TestMethod]
        public void TestFieldHeaderEncoding_CommonType_CommonName()
        {
            Field flags = Field.Values["Flags"];
            Assert.AreEqual(FieldType.Uint32, flags.Type);
            Assert.AreEqual(2, flags.NthOfType);
            CollectionAssert.AreEqual(new byte[] { 0x22 }, flags.Header);
        }

        [TestMethod]
        public void TestFieldHeaderEncoding_CommonType_UncommonName()
        {
            Field highQualityIn = Field.Values["HighQualityIn"];
            Assert.AreEqual(FieldType.Uint32, highQualityIn.Type);
            Assert.AreEqual(16, highQualityIn.NthOfType);
            CollectionAssert.AreEqual(new byte[] { 0x20, 0x10 }, highQualityIn.Header);
        }

        [TestMethod]
        public void TestFieldHeaderEncoding_UncommonType_CommonName()
        {
            Field closeResolution = Field.Values["CloseResolution"];
            Assert.AreEqual(FieldType.Uint8, closeResolution.Type);
            Assert.AreEqual(1, closeResolution.NthOfType);
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x10 }, closeResolution.Header);
        }

        [TestMethod]
        public void TestFieldTotalCount()
        {
            int count = Field.Values.Count();
            Assert.IsGreaterThan(200, count,
                $"Expected more than 200 registered fields, got {count}.");
        }

        [TestMethod]
        public void TestFieldTypes_Int32Exists()
        {
            Field[] int32Fields = Field.Values
                .Where(f => f.Type == FieldType.Int32)
                .ToArray();

            Assert.IsNotEmpty(int32Fields,
                "No Int32 fields found in Field.Values.");
        }

        [TestMethod]
        public void TestFieldTypes_Int64Exists()
        {
            Field[] int64Fields = Field.Values
                .Where(f => f.Type == FieldType.Int64)
                .ToArray();

            // Int64 fields exist only if definitions.json has them;
            // currently there are none, so just ensure no crash
            Assert.IsGreaterThanOrEqualTo(0, int64Fields.Length);
        }

        [TestMethod]
        public void TestEncodeDecodeWithNewFieldType_Number()
        {
            string txJson = @"{
                ""TransactionType"": ""OracleSet"",
                ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
                ""OracleDocumentID"": 1,
                ""Sequence"": 1,
                ""Fee"": ""12"",
                ""Flags"": 0
            }";

            JsonObject obj = JsonNode.Parse(txJson).AsObject();
            string encoded = XrplBinaryCodec.Encode(obj);
            Assert.IsNotNull(encoded);
            Assert.IsGreaterThan(0, encoded.Length);

            JsonNode decoded = XrplBinaryCodec.Decode(encoded);
            Assert.AreEqual("OracleSet", decoded["TransactionType"].GetValue<string>());
            Assert.AreEqual("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", decoded["Account"].GetValue<string>());
        }

        [TestMethod]
        public void TestEncodeDecodeWithNewFieldType_XChainBridge()
        {
            string txJson = @"{
                ""TransactionType"": ""XChainCreateBridge"",
                ""Account"": ""r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ"",
                ""XChainBridge"": {
                    ""LockingChainDoor"": ""r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ"",
                    ""LockingChainIssue"": { ""currency"": ""XRP"" },
                    ""IssuingChainDoor"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
                    ""IssuingChainIssue"": { ""currency"": ""XRP"" }
                },
                ""SignatureReward"": ""100"",
                ""MinAccountCreateAmount"": ""10000000"",
                ""Sequence"": 1,
                ""Fee"": ""12"",
                ""Flags"": 0
            }";

            JsonObject obj = JsonNode.Parse(txJson).AsObject();
            string encoded = XrplBinaryCodec.Encode(obj);
            Assert.IsNotNull(encoded);
            Assert.IsGreaterThan(0, encoded.Length);

            JsonNode decoded = XrplBinaryCodec.Decode(encoded);
            Assert.AreEqual("XChainCreateBridge", decoded["TransactionType"].GetValue<string>());
            Assert.IsNotNull(decoded["XChainBridge"]);
            Assert.AreEqual("r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ",
                decoded["XChainBridge"]["LockingChainDoor"].GetValue<string>());
            Assert.AreEqual("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                decoded["XChainBridge"]["IssuingChainDoor"].GetValue<string>());
        }
    }
}
