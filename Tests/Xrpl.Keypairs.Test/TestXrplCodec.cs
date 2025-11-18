using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.AddressCodec;
using Xrpl.Keypairs;
using static Xrpl.AddressCodec.XrplCodec;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-keypairs/test/xrp-codec-test.js

namespace Xrpl.Keypairs.Tests
{
    [TestClass]
    public class TestUEncodeSeed
    {
        [TestMethod]
        public void EncodeSECPSeed()
        {
            string result = XrplCodec.EncodeSeed("CF2DE378FBDD7E2EE87D486DFB5A7BFF".FromHex(), "secp256k1");
            Assert.AreEqual("sn259rEFXrQrWyx3Q7XneWcwV6dfL", result);
        }

        [TestMethod]
        public void EncodeLowSECPSeed()
        {
            string result = XrplCodec.EncodeSeed("00000000000000000000000000000000".FromHex(), "secp256k1");
            Assert.AreEqual("sp6JS7f14BuwFY8Mw6bTtLKWauoUs", result);
        }

        [TestMethod]
        public void EncodeHighSECPSeed()
        {
            string result = XrplCodec.EncodeSeed("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF".FromHex(), "secp256k1");
            Assert.AreEqual("saGwBRReqUNKuWNLpUAq8i8NkXEPN", result);
        }

        [TestMethod]
        public void EncodeEDSeed()
        {
            string result = XrplCodec.EncodeSeed("4C3A1D213FBDFB14C7C28D609469B341".FromHex(), "ed25519");
            Assert.AreEqual("sEdTM1uX8pu2do5XvTnutH6HsouMaM2", result);
        }

        [TestMethod]
        public void EncodeLowEDSeed()
        {
            string result = XrplCodec.EncodeSeed("00000000000000000000000000000000".FromHex(), "ed25519");
            Assert.AreEqual("sEdSJHS4oiAdz7w2X2ni1gFiqtbJHqE", result);
        }

        [TestMethod]
        public void EncodeHighEDSeed()
        {
            string result = XrplCodec.EncodeSeed("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF".FromHex(), "ed25519");
            Assert.AreEqual("sEdV19BLfeQeKdEXyYA4NhjPJe6XBfG", result);
        }
    }

    [TestClass]
    public class TestUDecodeSeed
    {
        [TestMethod]
        public void DecodeEDSeed()
        {
            DecodedSeed decodedSeed = XrplCodec.DecodeSeed("sEdTM1uX8pu2do5XvTnutH6HsouMaM2");
            Assert.AreEqual("4C3A1D213FBDFB14C7C28D609469B341", decodedSeed.Bytes.ToHex());
            Assert.AreEqual("ed25519", decodedSeed.Type);
        }

        [TestMethod]
        public void DecodeSECPSeed()
        {
            DecodedSeed decodedSeed = XrplCodec.DecodeSeed("sn259rEFXrQrWyx3Q7XneWcwV6dfL");
            Assert.AreEqual("CF2DE378FBDD7E2EE87D486DFB5A7BFF", decodedSeed.Bytes.ToHex());
            Assert.AreEqual("secp256k1", decodedSeed.Type);
        }
    }

    [TestClass]
    public class TestUEncodeAccountID
    {
        [TestMethod]
        public void EncodeAccountID()
        {
            string result = XrplCodec.EncodeAccountID("BA8E78626EE42C41B46D46C3048DF3A1C3C87072".FromHex());
            Assert.AreEqual("rJrRMgiRgrU6hDF4pgu5DXQdWyPbY35ErN", result);
        }
    }

    [TestClass]
    public class TestUDecodeNodePublic
    {
        [TestMethod]
        public void DecodeNodePublic()
        {
            byte[] result = XrplCodec.DecodeNodePublic("n9MXXueo837zYH36DvMc13BwHcqtfAWNJY5czWVbp7uYTj7x17TH");
            Assert.AreEqual("0388E5BA87A000CB807240DF8C848EB0B5FFA5C8E5A521BC8E105C0F0A44217828", result.ToHex());
        }
    }
}