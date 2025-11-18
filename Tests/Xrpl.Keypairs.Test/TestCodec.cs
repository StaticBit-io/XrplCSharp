using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.AddressCodec;
using static Xrpl.AddressCodec.XrplCodec;
using Xrpl.Keypairs;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-keypairs/test/codec-test.js

namespace Xrpl.Keypairs.Tests
{
    [TestClass]
    public class TestUCodec
    {

        [TestMethod]
        public void TestKeypairsEncodeAccountID()
        {
            string actual = XrplCodec.EncodeAccountID("BA8E78626EE42C41B46D46C3048DF3A1C3C87072".FromHex());
            Assert.AreEqual("rJrRMgiRgrU6hDF4pgu5DXQdWyPbY35ErN", actual);
        }

        [TestMethod]
        public void TestKeypairsEncodeNodePublic()
        {
            string actual = XrplCodec.EncodeNodePublic("0388E5BA87A000CB807240DF8C848EB0B5FFA5C8E5A521BC8E105C0F0A44217828".FromHex());
            Assert.AreEqual("n9MXXueo837zYH36DvMc13BwHcqtfAWNJY5czWVbp7uYTj7x17TH", actual);
        }

        [TestMethod]
        public void TestDecodeSeed()
        {
            DecodedSeed decoded = XrplCodec.DecodeSeed("sEdTM1uX8pu2do5XvTnutH6HsouMaM2");
            Assert.AreEqual("4C3A1D213FBDFB14C7C28D609469B341", decoded.Bytes.ToHex());
            Assert.AreEqual("ed25519", decoded.Type);

            DecodedSeed decoded2 = XrplCodec.DecodeSeed("sn259rEFXrQrWyx3Q7XneWcwV6dfL");
            Assert.AreEqual("CF2DE378FBDD7E2EE87D486DFB5A7BFF", decoded2.Bytes.ToHex());
            Assert.AreEqual("secp256k1", decoded2.Type);
        }

        [TestMethod]
        public void TestEncodeSeedWithType()
        {
            string edSeed = "sEdTM1uX8pu2do5XvTnutH6HsouMaM2";
            DecodedSeed decoded = XrplCodec.DecodeSeed("sEdTM1uX8pu2do5XvTnutH6HsouMaM2");
            Assert.AreEqual("4C3A1D213FBDFB14C7C28D609469B341", decoded.Bytes.ToHex());
            Assert.AreEqual("ed25519", decoded.Type);
            Assert.AreEqual(XrplCodec.EncodeSeed(decoded.Bytes, decoded.Type), edSeed);
        }
    }
}