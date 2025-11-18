using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Keypairs;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-keypairs/test/utils-test.js

namespace Xrpl.Keypairs.Tests
{
    [TestClass]
    public class TestUUtils
    {
        public bool Equality(byte[] a1, byte[] b1)
        {
            int i;
            if (a1.Length == b1.Length)
            {
                i = 0;
                while (i < a1.Length && (a1[i] == b1[i])) //Earlier it was a1[i]!=b1[i]
                {
                    i++;
                }
                if (i == a1.Length)
                {
                    return true;
                }
            }

            return false;
        }

        [TestMethod]
        public void HexToBytesEmptyTest()
        {
            Assert.IsTrue(Equality("".FromHex(), new byte[0]));
        }

        [TestMethod]
        public void HexToBytesZeroTest()
        {
            Assert.IsTrue(Equality("000000".FromHex(), new byte[] { 0x0, 0x0, 0x0 }));
        }

        [TestMethod]
        public void HexToBytesDEEDBEEFTest()
        {
            Assert.IsTrue(Equality("DEADBEEF".FromHex(), new byte[] { 222, 173, 190, 239 }));
        }

        [TestMethod]
        public void BytesToHexDEEDBEEFTest()
        {
            Assert.AreEqual("DEADBEEF", new byte[] { 222, 173, 190, 239 }.ToHex());
        }
    }
}