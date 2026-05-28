using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib.Types
{
    [TestClass]
    public class TestIouValueTrailingDot
    {
        private static void AssertEquivalent(string withDot, string canonical)
        {
            IouValue a = IouValue.FromString(withDot);
            IouValue b = IouValue.FromString(canonical);

            Assert.AreEqual(b.Mantissa, a.Mantissa, "mantissa");
            Assert.AreEqual(b.Exponent, a.Exponent, "exponent");
            Assert.AreEqual(b.Precision, a.Precision, "precision");
            Assert.AreEqual(b.IsNegative, a.IsNegative, "sign");
            CollectionAssert.AreEqual(b.ToBytes(), a.ToBytes(), "ToBytes blob");
            Assert.AreEqual(b.ToString(), a.ToString(), "ToString");
        }

        [TestMethod]
        public void TrailingDot_LargeValue_EquivalentToNoDot()
        {
            // Repro of the XPmarket AMMDeposit case ("128700.").
            AssertEquivalent("128700.", "128700");
        }

        [TestMethod]
        public void TrailingDot_SingleDigit_EquivalentToNoDot()
        {
            AssertEquivalent("1.", "1");
        }

        [TestMethod]
        public void TrailingDot_NegativeValue_EquivalentToNoDot()
        {
            AssertEquivalent("-42.", "-42");
        }

        [TestMethod]
        public void TrailingDot_ToBytesMatchesCanonical()
        {
            byte[] withDot = IouValue.FromString("128700.").ToBytes();
            byte[] canonical = IouValue.FromString("128700").ToBytes();
            CollectionAssert.AreEqual(canonical, withDot);
        }

        [TestMethod]
        [DataRow("100")]
        [DataRow("100.50")]
        [DataRow("0")]
        [DataRow("1.5e10")]
        [DataRow("-0.001")]
        public void ExistingValues_RoundTripUnchanged(string value)
        {
            // Regression guard: previously valid values must parse and round-trip to themselves.
            IouValue parsed = IouValue.FromString(value);
            string serialized = parsed.ToString();
            IouValue reparsed = IouValue.FromString(serialized);

            Assert.AreEqual(parsed.Mantissa, reparsed.Mantissa, "mantissa");
            Assert.AreEqual(parsed.Exponent, reparsed.Exponent, "exponent");
            Assert.AreEqual(parsed.Precision, reparsed.Precision, "precision");
            CollectionAssert.AreEqual(parsed.ToBytes(), reparsed.ToBytes(), "ToBytes blob");
        }
    }
}
