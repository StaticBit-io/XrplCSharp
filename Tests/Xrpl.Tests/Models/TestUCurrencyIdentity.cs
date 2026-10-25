using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Models.Common;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// What makes two <see cref="Currency"/> amounts the same asset, and what <see cref="CurrencyExtensions.ToIssued"/> keeps.
    /// </summary>
    [TestClass]
    public class TestUCurrencyIdentity
    {
        private const string MptA = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        private const string MptB = "00000002A407AF5856CCF3C42619DAA925813FC955C72983";

        private static Currency Mpt(string id, string value = "10") => new Currency { MPTokenIssuanceID = id, Value = value };

        [TestMethod]
        public void Equals_DifferentMptIssuances_AreNotEqual()
        {
            Assert.IsFalse(Mpt(MptA).Equals(Mpt(MptB)));
            Assert.IsTrue(Mpt(MptA) != Mpt(MptB));
        }

        [TestMethod]
        public void Equals_SameMptIssuance_IsEqualWhateverTheValueAndCase()
        {
            Currency upper = Mpt(MptA, "10");
            Currency lower = Mpt(MptA.ToLowerInvariant(), "20");

            Assert.IsTrue(upper == lower);
            Assert.AreEqual(upper.GetHashCode(), lower.GetHashCode());
        }

        [TestMethod]
        public void Equals_MptAndXrp_AreNotEqual()
        {
            Currency xrp = new Currency { Value = "10" };
            Currency mptWithoutCode = Mpt(MptA);

            Assert.IsFalse(xrp.Equals(mptWithoutCode));
        }

        [TestMethod]
        public void GetHashCode_EqualIssuedCurrencies_HashTheSame()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "1" };
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "2" };

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            HashSet<Currency> set = new HashSet<Currency> { a, b, Mpt(MptA), Mpt(MptB) };
            Assert.AreEqual(3, set.Count);
        }

        [TestMethod]
        public void ToIssued_Mpt_KeepsTheIssuanceId()
        {
            global::Xrpl.Models.Common.Common.IssuedCurrency issued = Mpt(MptA).ToIssued();

            Assert.AreEqual(MptA, issued.MptIssuanceId);
            Assert.IsTrue(issued.IsMpt());
            Assert.IsFalse(issued.IsXrp());
        }

        [TestMethod]
        public void ToIssued_IssuedCurrency_KeepsCodeAndIssuer()
        {
            global::Xrpl.Models.Common.Common.IssuedCurrency issued = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "1" }.ToIssued();

            Assert.AreEqual("USD", issued.Currency);
            Assert.AreEqual("rIssuer", issued.Issuer);
            Assert.IsNull(issued.MptIssuanceId);
        }
    }
}
