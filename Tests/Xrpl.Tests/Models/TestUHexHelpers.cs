using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.Models.Utils;
using Xrpl.Utils;
using Xrpl.Utils.Hashes;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Pinning tests for the unified hex helpers (#40): one byte-level pair
    /// (AddressCodec.Utils.ToHex/FromHex), one string-level pair
    /// (StringConversion + HexStringHelper), UPPERCASE output everywhere —
    /// the case rippled emits in JSON. Guards the consolidation against
    /// silent behavior drift.
    /// </summary>
    [TestClass]
    public class TestUHexHelpers
    {
        [TestMethod]
        public void TestUConvertStringToHex_EmitsUppercase()
        {
            Assert.AreEqual("426974636F696E", "Bitcoin".ConvertStringToHex());
            Assert.AreEqual("687474703A2F2F7872706C2E6F7267", "http://xrpl.org".ConvertStringToHex());
        }

        [TestMethod]
        public void TestUFromHexString_AcceptsBothCases_NoNullTrim()
        {
            Assert.AreEqual("Bitcoin", "426974636f696e".FromHexString());
            Assert.AreEqual("Bitcoin", "426974636F696E".FromHexString());
            // Variable-length fields must round-trip bytes exactly:
            // trailing null bytes survive the text conversion
            Assert.AreEqual("AB\0\0", "41420000".FromHexString());
            Assert.AreEqual(4, "41420000".FromHexString().Length);
            Assert.IsNull("".FromHexString());
            Assert.IsNull(((string)null).FromHexString());
        }

        [TestMethod]
        public void TestUHexStringHelper_FromHex_TrimsByDefault()
        {
            // Zero-padded fixed-size fields: decoding stops at the first null
            Assert.AreEqual("AB", HexStringHelper.FromHex("41420000"));
            Assert.AreEqual("AB\0\0", HexStringHelper.FromHex("41420000", trimTrailingNulls: false));
        }

        [TestMethod]
        public void TestUBytesRoundTrip_UppercaseOut_AnyCaseIn()
        {
            byte[] bytes = { 0xDE, 0xAD, 0xBE, 0xEF };
            Assert.AreEqual("DEADBEEF", bytes.ToHex());
            CollectionAssert.AreEqual(bytes, "deadbeef".FromHex());
            CollectionAssert.AreEqual(bytes, "DEADBEEF".FromHex());
        }

        [TestMethod]
        public void TestUCurrencyToHex_UppercasePadded()
        {
            // 3-char standard codes stay as-is; longer codes become 40-char UPPER hex
            Assert.AreEqual("USD", "USD".CurrencyToHex());
            Assert.AreEqual("426974636F696E00000000000000000000000000", "Bitcoin".CurrencyToHex());
        }

        [TestMethod]
        public void TestUIsHexCurrencyCode_Anchored()
        {
            string valid = new string('A', 40);
            Assert.IsTrue(valid.IsHexCurrencyCode());
            Assert.IsTrue(valid.ToLowerInvariant().IsHexCurrencyCode());
            // 41 chars containing 40 hex must NOT pass (the old regex lacked anchors)
            Assert.IsFalse((valid + "Z").IsHexCurrencyCode());
            Assert.IsFalse(("Z" + valid).IsHexCurrencyCode());
            Assert.IsFalse(new string('A', 39).IsHexCurrencyCode());
            // .NET $ matches before a trailing newline - \z must reject it
            Assert.IsFalse((valid + "\n").IsHexCurrencyCode());
        }

        [TestMethod]
        public void TestUNormalizeToHex_MatchesConvertStringToHex()
        {
            // The two text→hex entry points must agree byte-for-byte and in case
            Assert.AreEqual("Bitcoin".ConvertStringToHex(), HexStringHelper.NormalizeToHex("Bitcoin"));
        }
    }
}
