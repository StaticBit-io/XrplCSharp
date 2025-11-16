using Microsoft.VisualStudio.TestTools.UnitTesting;

using XrplTests;

using static Xrpl.AddressCodec.XrplCodec;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-address-codec/src/xrp-codec.test.js

namespace Xrpl.AddressCodec.Tests
{
    [TestClass]
    public class TestUMiscXrplCodec
    {
        public void EncodeDecodeAccountIDTest(string base58, string hex)
        {
            string actual = XrplCodec.EncodeAccountID(hex.FromHex());
            Assert.AreEqual(base58, actual);
            byte[] buffer = XrplCodec.DecodeAccountID(base58);
            Assert.AreEqual(buffer.ToHex(), hex);
        }

        [TestMethod]
        public void TestEncodeDecodeAccountID()
        {
            EncodeDecodeAccountIDTest(
                "rJrRMgiRgrU6hDF4pgu5DXQdWyPbY35ErN",
                "BA8E78626EE42C41B46D46C3048DF3A1C3C87072"
            );
        }

        public void EncodeDecodeNodePublicTest(string base58, string hex)
        {
            string actual = XrplCodec.EncodeNodePublic(hex.FromHex());
            Assert.AreEqual(base58, actual);
            byte[] buffer = XrplCodec.DecodeNodePublic(base58);
            Assert.AreEqual(buffer.ToHex(), hex);
        }

        [TestMethod]
        public void TestEncodeDecodeNodePublic()
        {
            EncodeDecodeNodePublicTest(
                "n9MXXueo837zYH36DvMc13BwHcqtfAWNJY5czWVbp7uYTj7x17TH",
                "0388E5BA87A000CB807240DF8C848EB0B5FFA5C8E5A521BC8E105C0F0A44217828"
            );
        }

        public void EncodeDecodeAccountPublicTest(string base58, string hex)
        {
            string actual = XrplCodec.EncodeAccountPublic(hex.FromHex());
            Assert.AreEqual(base58, actual);
            byte[] buffer = XrplCodec.DecodeAccountPublic(base58);
            Assert.AreEqual(buffer.ToHex(), hex);
        }

        [TestMethod]
        public void TestEncodeDecodeAccountPublic()
        {
            EncodeDecodeAccountPublicTest(
                "aB44YfzW24VDEJQ2UuLPV2PvqcPCSoLnL7y5M1EzhdW4LnK5xMS3",
                "023693F15967AE357D0327974AD46FE3C127113B1110D6044FD41E723689F81CC6"
            );
        }

        [TestMethod]
        public void TestDecodeArbitrarySeed()
        {
            XrplCodec.DecodedSeed decoded = XrplCodec.DecodeSeed("sEdTM1uX8pu2do5XvTnutH6HsouMaM2");
            Assert.AreEqual("4C3A1D213FBDFB14C7C28D609469B341", decoded.Bytes.ToHex());
            Assert.AreEqual("ed25519", decoded.Type);

            XrplCodec.DecodedSeed decoded1 = XrplCodec.DecodeSeed("sn259rEFXrQrWyx3Q7XneWcwV6dfL");
            Assert.AreEqual("CF2DE378FBDD7E2EE87D486DFB5A7BFF", decoded1.Bytes.ToHex());
            Assert.AreEqual("secp256k1", decoded1.Type);
        }

        [TestMethod]
        public void TestDecodeTypeSeed()
        {
            string edSeed = "sEdTM1uX8pu2do5XvTnutH6HsouMaM2";
            XrplCodec.DecodedSeed decoded = XrplCodec.DecodeSeed(edSeed);
            string type = "ed25519";
            Assert.AreEqual("4C3A1D213FBDFB14C7C28D609469B341", decoded.Bytes.ToHex());
            Assert.AreEqual(decoded.Type, type);
            Assert.AreEqual(XrplCodec.EncodeSeed(decoded.Bytes, type), edSeed);
        }

        [TestMethod]
        public void TestValidClassicSECP()
        {
            Assert.IsTrue(XrplCodec.IsValidClassicAddress("rU6K7V3Po4snVhBBaU29sesqs2qTQJWDw1"));
        }

        [TestMethod]
        public void TestValidClassicED()
        {
            Assert.IsTrue(XrplCodec.IsValidClassicAddress("rLUEXYuLiQptky37CqLcm9USQpPiz5rkpD"));
        }

        [TestMethod]
        public void TestInvalidClassic()
        {
            Assert.IsFalse(XrplCodec.IsValidClassicAddress("rU6K7V3Po4snVhBBaU29sesqs2qTQJWDw2"));
        }

        [TestMethod]
        public void TestInvalidClassicEmpty()
        {
            Assert.IsFalse(XrplCodec.IsValidClassicAddress(""));
        }
    }

    [TestClass]
    public class TestUEncodeXrplCodec
    {
        [TestMethod]
        public void TestEncodeSECP()
        {
            string result = XrplCodec.EncodeSeed("CF2DE378FBDD7E2EE87D486DFB5A7BFF".FromHex(), "secp256k1");
            Assert.AreEqual("sn259rEFXrQrWyx3Q7XneWcwV6dfL", result);
        }

        [TestMethod]
        public void TestEncodeLowSECP()
        {
            string result = XrplCodec.EncodeSeed("00000000000000000000000000000000".FromHex(), "secp256k1");
            Assert.AreEqual("sp6JS7f14BuwFY8Mw6bTtLKWauoUs", result);
        }

        [TestMethod]
        public void TestEncodeHighSECP()
        {
            string result = XrplCodec.EncodeSeed("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF".FromHex(), "secp256k1");
            Assert.AreEqual("saGwBRReqUNKuWNLpUAq8i8NkXEPN", result);
        }

        [TestMethod]
        public void TestEncodeED()
        {
            string result = XrplCodec.EncodeSeed("4C3A1D213FBDFB14C7C28D609469B341".FromHex(), "ed25519");
            Assert.AreEqual("sEdTM1uX8pu2do5XvTnutH6HsouMaM2", result);
        }

        [TestMethod]
        public void TestEncodeLowED()
        {
            string result = XrplCodec.EncodeSeed("00000000000000000000000000000000".FromHex(), "ed25519");
            Assert.AreEqual("sEdSJHS4oiAdz7w2X2ni1gFiqtbJHqE", result);
        }

        [TestMethod]
        public void TestEncodeHighED()
        {
            string result = XrplCodec.EncodeSeed("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF".FromHex(), "ed25519");
            Assert.AreEqual("sEdV19BLfeQeKdEXyYA4NhjPJe6XBfG", result);
        }

        [TestMethod]
        public void TestSeedLess16Bytes()
        {
            var ex = Helper.Throws<EncodingFormatException>(() =>
                XrplCodec.EncodeSeed("CF2DE378FBDD7E2EE87D486DFB5A7B".FromHex(), "secp256k1")
            );
        }

        [TestMethod]
        public void TestSeedGreater16Bytes()
        {
            var ex = Helper.Throws<EncodingFormatException>(() =>
                XrplCodec.EncodeSeed("CF2DE378FBDD7E2EE87D486DFB5A7BFFFF".FromHex(), "secp256k1")
            );
        }
    }

    [TestClass]
    public class TestUDecodeXrplCodec
    {
        [TestMethod]
        public void TestEncodeED()
        {
            XrplCodec.DecodedSeed decoded = XrplCodec.DecodeSeed("sEdTM1uX8pu2do5XvTnutH6HsouMaM2");
            Assert.AreEqual("4C3A1D213FBDFB14C7C28D609469B341", decoded.Bytes.ToHex());
            Assert.AreEqual("ed25519", decoded.Type);
        }

        [TestMethod]
        public void TestEncodeSECP()
        {
            XrplCodec.DecodedSeed decoded = XrplCodec.DecodeSeed("sn259rEFXrQrWyx3Q7XneWcwV6dfL");
            Assert.AreEqual("CF2DE378FBDD7E2EE87D486DFB5A7BFF", decoded.Bytes.ToHex());
            Assert.AreEqual("secp256k1", decoded.Type);
        }
    }

    [TestClass]
    public class TestUEncodeAccountIDXrplCodec
    {
        [TestMethod]
        public void TestEncodeAccountID()
        {
            string encoded = XrplCodec.EncodeAccountID("BA8E78626EE42C41B46D46C3048DF3A1C3C87072".FromHex());
            Assert.AreEqual("rJrRMgiRgrU6hDF4pgu5DXQdWyPbY35ErN", encoded);
        }

        [TestMethod]
        public void TestInvalidAccountID()
        {
            var ex = Helper.Throws<EncodingFormatException>(() =>
                XrplCodec.EncodeAccountID("ABCDEF".FromHex())
            );
        }
    }

    [TestClass]
    public class TestUDecodeNodePublic
    {
        [TestMethod]
        public void TestDecodeNodePublic()
        {
            byte[] decoded = XrplCodec.DecodeNodePublic("n9MXXueo837zYH36DvMc13BwHcqtfAWNJY5czWVbp7uYTj7x17TH");
            Assert.AreEqual("0388E5BA87A000CB807240DF8C848EB0B5FFA5C8E5A521BC8E105C0F0A44217828", decoded.ToHex());
        }
    }

    // TODO: Add missing tests and uncomment/fix errors generated
    [TestClass]
    public class TestUEncodeDecode
    {

        //private static readonly B58 B58;
        //[TestMethod]
        //public void TestEncode123456789()
        //{
        //    B58.Version version = B58.Version.With(versionByte: 0, expectedLength: 9);
        //    byte[] bytes = Encoding.ASCII.GetBytes("123456789");
        //    Assert.AreEqual(B58.Encode(bytes, version), "rnaC7gW34M77Kneb78s");
        //}

        //[TestMethod]
        //public void TestDecodeExpectedLen()
        //{
        //    B58.Version version = B58.Version.With(versionByte: 0, expectedLength: 9);
        //    Assert.AreEqual(B58.Decode("123456789", version), "rnaC7gW34M77Kneb78s");
        //}

        //[TestMethod]
        //public void TestDecodeInvalidLenUnder()
        //{
        //    B58.Version version = B58.Version.With(versionByte: 0, expectedLength: 8);
        //    Assert.AreEqual(B58.Decode("rnaC7gW34M77Kneb78s", version), "rnaC7gW34M77Kneb78s");
        //}

        //[TestMethod]
        //public void TestDecodeInvalidLenOver()
        //{
        //    B58.Version version = B58.Version.With(versionByte: 0, expectedLength: 10);
        //    Assert.AreEqual(B58.Decode("rnaC7gW34M77Kneb78s", version), "rnaC7gW34M77Kneb78s");
        //}
    }
}