using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/types/test_account_id.py

namespace XrplTests.BinaryCodecLib.Types
{
    [TestClass]
    public class TestUAccountID
    {
        static string HEX_ENCODING = "5E7B112523F68D2F5E879DB4EAC51C6698A69304";
        static string BASE58_ENCODING = "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59";

        [TestMethod]
        public void TestFromValueHex()
        {
            AccountId accountId = AccountId.FromValue(TestUAccountID.HEX_ENCODING);
            Assert.AreEqual(accountId.ToJson().GetValue<string>(), TestUAccountID.BASE58_ENCODING);
        }

        [TestMethod]
        public void TestFromValueBase58()
        {
            AccountId accountId = AccountId.FromValue(TestUAccountID.BASE58_ENCODING);
            Assert.AreEqual(accountId.ToString(), TestUAccountID.BASE58_ENCODING);
        }
    }
}

