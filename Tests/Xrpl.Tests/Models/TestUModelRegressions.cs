using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text;

using Xrpl.Models.Common;
using Xrpl.Models;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUModelRegressions
    {
        [TestMethod]
        public void TestUEscrowFinish_ParameterizedConstructor_SetsTheTransactionType()
        {
            EscrowFinish finish = new EscrowFinish("rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", 7, null, null);

            Assert.AreEqual(TransactionType.EscrowFinish, finish.TransactionType);
            StringAssert.Contains(finish.ToJson(), "\"TransactionType\":\"EscrowFinish\"");
        }

        [TestMethod]
        public void TestUNormalizeCurrencyCode_Xls16WithMultiByteText_DoesNotThrow()
        {
            // 02, seven zero bytes, then twelve bytes of text: "é" is two bytes in UTF-8, so the
            // text decodes to eleven characters.
            byte[] code = new byte[20];
            code[0] = 0x02;
            Encoding.UTF8.GetBytes("éABCDEFGHIJ").CopyTo(code, 8);
            string hex = System.Convert.ToHexString(code);

            Assert.AreEqual("éABCDEFGHIJ", hex.NormalizeCurrencyCode());
            Assert.AreEqual("éABC", hex.NormalizeCurrencyCode(4));
        }

        [TestMethod]
        public void TestUNormalizeCurrencyCode_Xls16WithAsciiText_ReadsBytesEightToTwenty()
        {
            byte[] code = new byte[20];
            code[0] = 0x02;
            for (int i = 1; i < 8; i++)
                code[i] = 0xAB;
            Encoding.ASCII.GetBytes("NFTCOLLECT12").CopyTo(code, 8);

            Assert.AreEqual("NFTCOLLECT12", System.Convert.ToHexString(code).NormalizeCurrencyCode());
        }
    }
}
