using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Enums;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/test_definition_service.py

namespace Xrpl.BinaryCodec.Tests
{
    [TestClass]
    public class TestUDefinitions
    {
        private const string TestFieldName = "Sequence";

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            StObject.FromJson(new JsonObject());
        }

        [TestMethod]
        public void TestFieldExists()
        {
            Assert.IsTrue(Field.Values.Has(TestFieldName));
        }

        [TestMethod]
        public void TestGetFieldTypeName()
        {
            Field field = Field.Values[TestFieldName];
            Assert.AreEqual("Uint32", field.Type.Name);
        }

        [TestMethod]
        public void TestGetFieldTypeCode()
        {
            Field field = Field.Values[TestFieldName];
            Assert.AreEqual(2, field.Type.Ordinal);
        }

        [TestMethod]
        public void TestGetFieldCode()
        {
            Field field = Field.Values[TestFieldName];
            Assert.AreEqual(4, field.NthOfType);
        }

        [TestMethod]
        public void TestGetFieldHeader()
        {
            Field field = Field.Values[TestFieldName];
            // Sequence: type=2 (Uint32), nth=4 => header = (2<<4)|4 = 0x24
            CollectionAssert.AreEqual(new byte[] { 0x24 }, field.Header);
        }

        [TestMethod]
        public void TestGetFieldNameFromHeader()
        {
            // header 0x24 => type=2, nth=4 => ordinal = (2<<16)|4
            int ordinal = (2 << 16) | 4;
            Field field = Field.Values[ordinal];
            Assert.AreEqual(TestFieldName, field.Name);
        }

        [TestMethod]
        public void TestInverseTransactionTypeMap()
        {
            TransactionType tt = TransactionType.Values["OfferCancel"];
            Assert.AreEqual(8, tt.Ordinal);

            TransactionType byOrdinal = TransactionType.Values[8];
            Assert.AreEqual("OfferCancel", byOrdinal.Name);
        }

        [TestMethod]
        public void TestInverseTransactionResultMap()
        {
            EngineResult er = (EngineResult)EngineResult.Values["tesSUCCESS"];
            Assert.AreEqual(0, er.Ordinal);

            EngineResult byOrdinal = (EngineResult)EngineResult.Values[0];
            Assert.AreEqual("tesSUCCESS", byOrdinal.Name);
        }

        [TestMethod]
        public void TestFieldTypes_KnownMappings()
        {
            Assert.AreEqual(FieldType.Uint32, Field.Values["Flags"].Type);
            Assert.AreEqual(FieldType.Amount, Field.Values["Amount"].Type);
            Assert.AreEqual(FieldType.AccountId, Field.Values["Account"].Type);
            Assert.AreEqual(FieldType.Hash256, Field.Values["PreviousTxnID"].Type);
            Assert.AreEqual(FieldType.Blob, Field.Values["SigningPubKey"].Type);
            Assert.AreEqual(FieldType.StArray, Field.Values["Memos"].Type);
        }

        [TestMethod]
        public void TestFieldIsSigningField()
        {
            Assert.IsTrue(Field.Values["Sequence"].IsSigningField);
            Assert.IsTrue(Field.Values["Amount"].IsSigningField);
            Assert.IsFalse(Field.Values["TxnSignature"].IsSigningField);
        }

        [TestMethod]
        public void TestFieldIsVlEncoded()
        {
            Assert.IsTrue(Field.Values["SigningPubKey"].IsVlEncoded);
            Assert.IsTrue(Field.Values["TxnSignature"].IsVlEncoded);
            Assert.IsTrue(Field.Values["Account"].IsVlEncoded);
            Assert.IsFalse(Field.Values["Flags"].IsVlEncoded);
            Assert.IsFalse(Field.Values["Amount"].IsVlEncoded);
        }
    }
}
