using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/types/test_uint.py

namespace XrplTests.BinaryCodecLib.Types
{
    [TestClass]
    public class TestUInt
    {

        [TestMethod]
        public void TestFromValue()
        {
            Uint8 value1 = Uint8.FromValue(124);
            Uint8 value2 = Uint8.FromValue(123);
            Uint8 value3 = Uint8.FromValue(124);
            Assert.IsGreaterThan((int)value2.ToJson(), (int)value1.ToJson());
            Assert.IsLessThan((int)value1.ToJson(), (int)value2.ToJson());
            Assert.AreNotEqual((int)value1.ToJson(), (int)value2.ToJson());
            Assert.AreEqual(value1.ToJson(), value3.ToJson());
        }

        [TestMethod]
        public void TestFromValue64()
        {
            Uint64 value1 = Uint64.FromValue("10000000");
            Assert.AreEqual("1000000000000000", value1.ToJson());
        }

        [TestMethod]
        public void TestCompare()
        {
            Uint8 value1 = Uint8.FromValue(124);
            Assert.AreEqual(124, (int)value1.ToJson());
            Assert.IsLessThan(125, (int)value1.ToJson());
            Assert.IsGreaterThan(123, (int)value1.ToJson());
        }

        [TestMethod]
        public void TestCompareDifferent()
        {
            int cconst = 124;
            string sstring = "000000000000007C";
            Uint8 uint8 = Uint8.FromValue(cconst);
            Uint16 uint16 = Uint16.FromValue(cconst);
            Uint32 uint32 = Uint32.FromValue(cconst);
            Uint64 uint64 = Uint64.FromValue(cconst);
            Assert.AreEqual((int)uint8.Value, (int)cconst);
            Assert.AreEqual((int)uint16.Value, (int)cconst);
            Assert.AreEqual((int)uint32.Value, (int)cconst);
            Assert.AreEqual((int)uint64.Value, (int)cconst);
            Assert.AreEqual(uint64.ToString(), sstring);
        }
    }
}

