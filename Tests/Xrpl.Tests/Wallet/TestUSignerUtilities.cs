using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
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
            var signers = new JArray();
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
            var signers = new JArray
            {
                new JObject
                {
                    ["Signer"] = new JObject
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
            var signers = new JArray
            {
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                },
                new JObject
                {
                    ["Signer"] = new JObject
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
            var signers = new JArray
            {
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                },
                new JObject
                {
                    ["Signer"] = new JObject
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
            var signers = new JArray
            {
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress2,
                        ["SigningPubKey"] = "PUBKEY2",
                        ["TxnSignature"] = "SIG2"
                    }
                },
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress1,
                        ["SigningPubKey"] = "PUBKEY1",
                        ["TxnSignature"] = "SIG1"
                    }
                }
            };
            var result = SignerUtilities.DedupeAndSortSigners(signers);
            Assert.AreEqual(2, result.Count);

            var bytes1 = SignerUtilities.GetAccountIdBytes((string)result[0]["Signer"]["Account"]);
            var bytes2 = SignerUtilities.GetAccountIdBytes((string)result[1]["Signer"]["Account"]);
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(bytes1, bytes2) < 0);
        }

        [TestMethod]
        public void TestUDedupeAndSortSigners_PreservesWrapperStructure()
        {
            var signers = new JArray
            {
                new JObject
                {
                    ["Signer"] = new JObject
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
            var signers = new JArray
            {
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress3
                    }
                },
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress1
                    }
                },
                new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = ClassicAddress2
                    }
                }
            };
            var result = SignerUtilities.SortSignersArray(signers);
            Assert.AreEqual(3, result.Count);

            var acc0 = (string)result[0]["Signer"]["Account"];
            var acc1 = (string)result[1]["Signer"]["Account"];
            var acc2 = (string)result[2]["Signer"]["Account"];

            var bytes0 = SignerUtilities.GetAccountIdBytes(acc0);
            var bytes1 = SignerUtilities.GetAccountIdBytes(acc1);
            var bytes2 = SignerUtilities.GetAccountIdBytes(acc2);

            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(bytes0, bytes1) <= 0);
            Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(bytes1, bytes2) <= 0);
        }

        #endregion

        #region ConvertJTokenToClrType Tests

        [TestMethod]
        public void TestUConvertJTokenToClrType_String()
        {
            var token = new JValue("test");
            var result = SignerUtilities.ConvertJTokenToClrType(token);
            Assert.AreEqual("test", result);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_Integer()
        {
            var token = new JValue(42);
            var result = SignerUtilities.ConvertJTokenToClrType(token);
            Assert.AreEqual(42L, result);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_Float()
        {
            var token = new JValue(3.14);
            var result = SignerUtilities.ConvertJTokenToClrType(token);
            Assert.AreEqual(3.14, result);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_Boolean()
        {
            var token = new JValue(true);
            var result = SignerUtilities.ConvertJTokenToClrType(token);
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_Null()
        {
            var token = JValue.CreateNull();
            var result = SignerUtilities.ConvertJTokenToClrType(token);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_Object()
        {
            var token = new JObject
            {
                ["key1"] = "value1",
                ["key2"] = 42
            };
            var result = SignerUtilities.ConvertJTokenToClrType(token) as Dictionary<string, dynamic>;
            Assert.IsNotNull(result);
            Assert.AreEqual("value1", result["key1"]);
            Assert.AreEqual(42L, result["key2"]);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_Array()
        {
            var token = new JArray { "a", "b", "c" };
            var result = SignerUtilities.ConvertJTokenToClrType(token) as List<dynamic>;
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("a", result[0]);
        }

        [TestMethod]
        public void TestUConvertJTokenToClrType_NestedObject()
        {
            var token = new JObject
            {
                ["outer"] = new JObject
                {
                    ["inner"] = "value"
                }
            };
            var result = SignerUtilities.ConvertJTokenToClrType(token) as Dictionary<string, dynamic>;
            Assert.IsNotNull(result);
            var outer = result["outer"] as Dictionary<string, dynamic>;
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
