using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    [TestClass]
    public class TestUSignerUtilities
    {
        private static readonly string ClassicAddress1 = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
        private static readonly string ClassicAddress2 = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";
        private static readonly string ClassicAddress3 = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";

        #region NormalizeClassicAddress Tests

        [TestMethod]
        public void TestUNormalizeClassicAddress_AlreadyClassic()
        {
            var result = SignerUtilities.NormalizeClassicAddress(ClassicAddress1);
            Assert.AreEqual(ClassicAddress1, result);
        }

        [TestMethod]
        public void TestUNormalizeClassicAddress_EmptyString()
        {
            var result = SignerUtilities.NormalizeClassicAddress("");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void TestUNormalizeClassicAddress_NullString()
        {
            var result = SignerUtilities.NormalizeClassicAddress(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestUNormalizeClassicAddress_WhitespaceString()
        {
            var result = SignerUtilities.NormalizeClassicAddress("   ");
            Assert.AreEqual("   ", result);
        }

        #endregion

        #region GetAccountIdBytes Tests

        [TestMethod]
        public void TestUGetAccountIdBytes_ValidAddress()
        {
            var bytes = SignerUtilities.GetAccountIdBytes(ClassicAddress1);
            Assert.IsNotNull(bytes);
            Assert.AreEqual(20, bytes.Length);
        }

        [TestMethod]
        public void TestUGetAccountIdBytes_DifferentAddresses()
        {
            var bytes1 = SignerUtilities.GetAccountIdBytes(ClassicAddress1);
            var bytes2 = SignerUtilities.GetAccountIdBytes(ClassicAddress2);
            CollectionAssert.AreNotEqual(bytes1, bytes2);
        }

        #endregion

        #region DedupeAndSortSigners Tests

        [TestMethod]
        public void TestUDedupeAndSortSigners_EmptyArray()
        {
            var signers = new JsonArray();
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_NullArray()
        {
            var result = SignerUtilities.DedupeAndSortSigners(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_SingleSigner()
        {
            var signers = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                }
            };
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_RemovesDuplicates()
        {
            var signers = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                },
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                }
            };
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_KeepsDifferentSignatures()
        {
            var signers = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                },
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG2"
                    }
                }
            };
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_SortsByAccountId()
        {
            var signers = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress2,
                        ["SigningPubKey"] = "PUBKEY2",
                        ["TxnSignature"] = "SIG2"
                    }
                },
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                }
            };
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.AreEqual(2, result.Count);

            var bytes1 = SignerUtilities.GetAccountIdBytes(result[0]["Signer"]["Account"].GetValue<string>());
            var bytes2 = SignerUtilities.GetAccountIdBytes(result[1]["Signer"]["Account"].GetValue<string>());
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(bytes1, bytes2) < 0);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_PreservesWrapperStructure()
        {
            var signers = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                }
            };
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.IsNotNull(result[0]["Signer"]);
        }

        #endregion

        #region SortSignersArray Tests

        [TestMethod]
        public void TestUSortSignersArray_SortsByAccountId()
        {
            var signers = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress3
                    }
                },
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress1
                    }
                },
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = ClassicAddress2
                    }
                }
            };
            var result = SignerUtilities.SortSignersArray(signers);
            Assert.AreEqual(3, result.Count);

            var acc0 = result[0]["Signer"]["Account"].GetValue<string>();
            var acc1 = result[1]["Signer"]["Account"].GetValue<string>();
            var acc2 = result[2]["Signer"]["Account"].GetValue<string>();

            var bytes0 = SignerUtilities.GetAccountIdBytes(acc0);
            var bytes1 = SignerUtilities.GetAccountIdBytes(acc1);
            var bytes2 = SignerUtilities.GetAccountIdBytes(acc2);

            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(bytes0, bytes1) <= 0);
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(bytes1, bytes2) <= 0);
        }

        #endregion

        #region ConvertJsonNodeToClrType Tests

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_String()
        {
            JsonNode token = JsonValue.Create("test");
            var result = SignerUtilities.ConvertJsonNodeToClrType(token);
            Assert.AreEqual("test", result);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_Integer()
        {
            JsonNode token = JsonValue.Create(42);
            var result = SignerUtilities.ConvertJsonNodeToClrType(token);
            Assert.AreEqual(42L, result);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_Float()
        {
            JsonNode token = JsonValue.Create(3.14);
            var result = SignerUtilities.ConvertJsonNodeToClrType(token);
            Assert.AreEqual(3.14, result);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_Boolean()
        {
            JsonNode token = JsonValue.Create(true);
            var result = SignerUtilities.ConvertJsonNodeToClrType(token);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_Null()
        {
            var result = SignerUtilities.ConvertJsonNodeToClrType(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_Object()
        {
            JsonNode token = new JsonObject
            {
                ["key1"] = "value1",
                ["key2"] = 42
            };
            var result = SignerUtilities.ConvertJsonNodeToClrType(token) as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual("value1", result["key1"]);
            Assert.AreEqual(42L, result["key2"]);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_Array()
        {
            JsonNode token = new JsonArray { JsonValue.Create("a"), JsonValue.Create("b"), JsonValue.Create("c") };
            var result = SignerUtilities.ConvertJsonNodeToClrType(token) as List<object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("a", result[0]);
        }

        [TestMethod]
        public void TestUConvertJsonNodeToClrType_NestedObject()
        {
            JsonNode token = new JsonObject
            {
                ["outer"] = new JsonObject
                {
                    ["inner"] = "value"
                }
            };
            var result = SignerUtilities.ConvertJsonNodeToClrType(token) as Dictionary<string, object>;
            Assert.IsNotNull(result);
            var outer = result["outer"] as Dictionary<string, object>;
            Assert.IsNotNull(outer);
            Assert.AreEqual("value", outer["inner"]);
        }

        #endregion

        #region ByteArrayComparer Tests

        [TestMethod]
        public void TestUByteArrayComparer_EqualArrays()
        {
            var a = new byte[] { 1, 2, 3 };
            var b = new byte[] { 1, 2, 3 };
            Assert.AreEqual(0, SignerUtilities.ByteArrayComparer.Instance.Compare(a, b));
        }

        [TestMethod]
        public void TestUByteArrayComparer_LessThan()
        {
            var a = new byte[] { 1, 2, 3 };
            var b = new byte[] { 1, 2, 4 };
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(a, b) < 0);
        }

        [TestMethod]
        public void TestUByteArrayComparer_GreaterThan()
        {
            var a = new byte[] { 1, 2, 4 };
            var b = new byte[] { 1, 2, 3 };
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(a, b) > 0);
        }

        [TestMethod]
        public void TestUByteArrayComparer_DifferentLengths()
        {
            var a = new byte[] { 1, 2 };
            var b = new byte[] { 1, 2, 3 };
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(a, b) < 0);
        }

        [TestMethod]
        public void TestUByteArrayComparer_NullArrays()
        {
            Assert.AreEqual(0, SignerUtilities.ByteArrayComparer.Instance.Compare(null, null));
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(null, new byte[] { 1 }) < 0);
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(new byte[] { 1 }, null) > 0);
        }

        #endregion
    }
}
