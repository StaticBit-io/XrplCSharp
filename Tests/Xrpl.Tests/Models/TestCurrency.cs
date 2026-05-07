using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Globalization;

using Xrpl.Models.Common;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCurrency
    {
        #region Round-trip ValueAsNumber (G16 fix verification)

        [TestMethod]
        public void ValueAsNumber_RoundTrip_16SignificantDigits_AmmLpToken()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "316227.7660168379" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("316227.7660168379", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_MaxMantissa()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "9999999999999999" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("9999999999999999", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_MinMantissa()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "1000000000000000" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("1000000000000000", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_15SignificantDigits()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "316227.766016838" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("316227.766016838", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_SmallValueWithLeadingZeros()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "0.001234567890123456" };
            decimal number = currency.ValueAsNumber;
            currency.ValueAsNumber = number;
            Assert.AreEqual("0.001234567890123456", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_ScientificNotation()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "1.234567890123456e10" };
            decimal number = currency.ValueAsNumber;
            Assert.AreEqual(12345678901.23456m, number);

            currency.ValueAsNumber = number;
            Assert.AreEqual("12345678901.23456", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_NegativeValue()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "-316227.7660168379" };
            decimal number = currency.ValueAsNumber;
            Assert.IsTrue(number < 0);

            currency.ValueAsNumber = number;
            Assert.AreEqual("-316227.7660168379", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_RoundTrip_Zero()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "0" };
            decimal number = currency.ValueAsNumber;
            Assert.AreEqual(0m, number);

            currency.ValueAsNumber = number;
            Assert.AreEqual("0", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_EmptyString_ReturnsZero()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "" };
            Assert.AreEqual(0m, currency.ValueAsNumber);
        }

        [TestMethod]
        public void ValueAsNumber_NullValue_ReturnsZero()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = null };
            Assert.AreEqual(0m, currency.ValueAsNumber);
        }

        [TestMethod]
        public void ValueAsNumber_Setter_UsesG0ForXrp()
        {
            Currency currency = new Currency { CurrencyCode = "XRP" };
            currency.ValueAsNumber = 1500000m;
            Assert.AreEqual("1500000", currency.Value);
        }

        [TestMethod]
        public void ValueAsNumber_16Digits_NeverRoundsUp()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "316227.7660168379" };
            decimal original = currency.ValueAsNumber;
            currency.ValueAsNumber = original;
            decimal afterRoundTrip = decimal.Parse(currency.Value, CultureInfo.InvariantCulture);
            Assert.IsTrue(afterRoundTrip <= original,
                $"Round-trip must not increase value: original={original}, afterRoundTrip={afterRoundTrip}");
        }

        #endregion

        #region ValueAsXrp

        [TestMethod]
        public void ValueAsXrp_SetValue_ConvertsToDrops()
        {
            Currency currency = new Currency();
            currency.ValueAsXrp = 1.5m;
            Assert.AreEqual("XRP", currency.CurrencyCode);
            Assert.AreEqual("1500000", currency.Value);
        }

        [TestMethod]
        public void ValueAsXrp_GetValue_ConvertsFromDrops()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Value = "1500000" };
            Assert.AreEqual(1.5m, currency.ValueAsXrp);
        }

        [TestMethod]
        public void ValueAsXrp_NonXrpCurrency_ReturnsNull()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "100" };
            Assert.IsNull(currency.ValueAsXrp);
        }

        [TestMethod]
        public void ValueAsXrp_SetNull_SetsValueToZero()
        {
            Currency currency = new Currency();
            currency.ValueAsXrp = null;
            Assert.AreEqual("0", currency.Value);
        }

        [TestMethod]
        public void ValueAsXrp_EmptyValue_ReturnsNull()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Value = "" };
            Assert.IsNull(currency.ValueAsXrp);
        }

        #endregion

        #region Implicit operators

        [TestMethod]
        public void ImplicitOperator_FromString()
        {
            Currency currency = "100.5";
            Assert.AreEqual("100.5", currency.Value);
        }

        [TestMethod]
        public void ImplicitOperator_FromDecimal()
        {
            Currency currency = 316227.7660168379m;
            Assert.AreEqual("316227.7660168379", currency.Value);
        }

        [TestMethod]
        public void ImplicitOperator_FromDouble()
        {
            Currency currency = 100.5;
            Assert.AreEqual("100.5", currency.Value);
        }

        [TestMethod]
        public void ImplicitOperator_FromInt()
        {
            Currency currency = 42;
            Assert.AreEqual("42", currency.Value);
        }

        #endregion

        #region CurrencyExtensions

        [TestMethod]
        public void GetValue_XrpCurrency_ReturnsXrpValue()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Value = "1500000" };
            decimal? value = currency.GetValue();
            Assert.AreEqual(1.5m, value);
        }

        [TestMethod]
        public void GetValue_TokenCurrency_ReturnsRawValue()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "123.456789" };
            decimal? value = currency.GetValue();
            Assert.AreEqual(123.456789m, value);
        }

        [TestMethod]
        public void GetValue_WithRound_RoundsCorrectly()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "123.456789" };
            decimal? value = currency.GetValue(round: 2);
            Assert.AreEqual(123.46m, value);
        }

        [TestMethod]
        public void GetValue_NullCurrency_ReturnsNull()
        {
            Currency currency = null;
            decimal? value = currency.GetValue();
            Assert.IsNull(value);
        }

        [TestMethod]
        public void IsXrp_XrpWithoutIssuer_ReturnsTrue()
        {
            Currency currency = new Currency { CurrencyCode = "XRP" };
            Assert.IsTrue(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_XrpWithIssuer_ReturnsFalse()
        {
            Currency currency = new Currency { CurrencyCode = "XRP", Issuer = "rSomeIssuer" };
            Assert.IsFalse(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_TokenCurrency_ReturnsFalse()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Assert.IsFalse(currency.IsXrp());
        }

        [TestMethod]
        public void IsXrp_NullCurrency_ReturnsFalse()
        {
            Currency currency = null;
            Assert.IsFalse(currency.IsXrp());
        }

        [TestMethod]
        public void IsLpToken_LpCurrencyCode_ReturnsTrue()
        {
            Currency currency = new Currency
            {
                CurrencyCode = "03AB1234000000000000000000000000000000AB",
                Issuer = "rAmmIssuer"
            };
            Assert.IsTrue(currency.IsLpToken());
        }

        [TestMethod]
        public void IsLpToken_RegularToken_ReturnsFalse()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Assert.IsFalse(currency.IsLpToken());
        }

        [TestMethod]
        public void NormalizeCurrencyCode_ThreeCharCode_ReturnsSame()
        {
            Assert.AreEqual("USD", "USD".NormalizeCurrencyCode());
            Assert.AreEqual("EUR", "EUR".NormalizeCurrencyCode());
        }

        [TestMethod]
        public void NormalizeCurrencyCode_LpToken_ReturnsLpPrefix()
        {
            string lpCode = "03AB1234000000000000000000000000000000AB";
            string result = lpCode.NormalizeCurrencyCode();
            Assert.IsTrue(result.StartsWith("LP "));
        }

        #endregion

        #region Equals and operators

        [TestMethod]
        public void Equals_SameCurrencyAndIssuer_DifferentValue_ReturnsTrue()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "100" };
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "200" };
            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
        }

        [TestMethod]
        public void Equals_DifferentIssuer_ReturnsFalse()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer1" };
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer2" };
            Assert.IsFalse(a.Equals(b));
            Assert.IsTrue(a != b);
        }

        [TestMethod]
        public void Equals_DifferentCurrencyCode_ReturnsFalse()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Currency b = new Currency { CurrencyCode = "EUR", Issuer = "rIssuer" };
            Assert.IsFalse(a.Equals(b));
        }

        [TestMethod]
        public void OperatorEquals_NullLeft_ReturnsFalse()
        {
            Currency a = null;
            Currency b = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Assert.IsFalse(a == b);
        }

        [TestMethod]
        public void OperatorEquals_NullRight_ReturnsFalse()
        {
            Currency a = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            Currency b = null;
            Assert.IsFalse(a == b);
        }

        [TestMethod]
        public void OperatorEquals_BothNull_ReturnsTrue()
        {
            Currency a = null;
            Currency b = null;
            Assert.IsTrue(a == b);
        }

        #endregion
    }
}
