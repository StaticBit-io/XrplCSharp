using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Globalization;

using System;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCurrency
    {
        #region Amounts outside decimal's range - issue #148

        private static Currency Iou(string value) =>
            new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = value };

        /// <summary>
        /// An amount too large for <c>decimal</c> is refused, whichever sign it carries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These used to do two different things. A positive one clamped to <c>decimal.MaxValue</c>
        /// and said nothing, so a balance of 1e96 was answered with 7.9e28 - wrong by 67 orders of
        /// magnitude, and wrong in a way that flowed onward into the caller's arithmetic. A
        /// negative one threw <c>FormatException</c>, because the fallback parse was missing
        /// <c>AllowLeadingSign</c> and could not read the minus.
        /// </para>
        /// <para>
        /// The threshold is not near the ledger's ceiling: 1e29 is barely above
        /// <c>decimal.MaxValue</c> and already unreachable.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_OutsideDecimalRange_Throws()
        {
            foreach (string value in new[]
                     {
                         "1e29", "-1e29",
                         "9e80", "-9e80",
                         "9999999999999999e80", "-9999999999999999e80",
                     })
            {
                Assert.ThrowsExactly<AmountOutOfRangeException>(
                    () => _ = Iou(value).ValueAsNumber,
                    $"'{value}' is a legitimate ledger amount that decimal cannot hold.");
            }
        }

        /// <summary>
        /// The exception carries the amount as the node sent it.
        /// </summary>
        /// <remarks>
        /// The point of refusing rather than clamping is that the real figure is still available;
        /// an exception that only said "too big" would trade one lost value for another.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_OutOfRangeException_CarriesTheOriginalValue()
        {
            AmountOutOfRangeException error = Assert.ThrowsExactly<AmountOutOfRangeException>(
                () => _ = Iou("-9999999999999999e80").ValueAsNumber);

            Assert.AreEqual("-9999999999999999e80", error.Value);
            Assert.Contains("-9999999999999999e80", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Negative amounts inside the range still parse, which is what makes the bound the bug.
        /// </summary>
        /// <remarks>
        /// Without this the test above would pass on an implementation that simply refused every
        /// negative amount - and negative balances are ordinary: a <c>RippleState</c> balance is
        /// negative from the low account's side.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_NegativeInsideRange_StillParses()
        {
            Assert.AreEqual(-100m, Iou("-100").ValueAsNumber);
            Assert.AreEqual(-0.00000000015m, Iou("-1.5e-10").ValueAsNumber);
            Assert.AreEqual(-79228162514264337593543950335m, Iou("-79228162514264337593543950335").ValueAsNumber);
        }

        /// <summary>
        /// An amount too small for <c>decimal</c> becomes zero rather than throwing.
        /// </summary>
        /// <remarks>
        /// The asymmetry with overflow is deliberate. The ledger goes down to 1e-81 and decimal
        /// stops near 1e-28, but a balance that small is zero at any scale a caller can act on, so
        /// failing over it would cost more than it protects. Overflow is the opposite: the number
        /// that would be returned is wrong by orders of magnitude and unsafe to use.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_BelowDecimalPrecision_IsZeroNotAnError()
        {
            Assert.AreEqual(0m, Iou("1e-96").ValueAsNumber);
            Assert.AreEqual(0m, Iou("-9999999999999999e-96").ValueAsNumber);
        }

        /// <summary>
        /// Something that is not a number is still a format error, not an out-of-range one.
        /// </summary>
        /// <remarks>
        /// The two are told apart by whether <c>double</c> can read the string: it spans the whole
        /// ledger range, so it succeeds exactly when the value is real and decimal merely cannot
        /// hold it. Reporting both the same way would hide a malformed response behind a message
        /// about magnitude.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_NotANumber_IsAFormatError()
        {
            Assert.ThrowsExactly<FormatException>(() => _ = Iou("abc").ValueAsNumber);
            Assert.ThrowsExactly<FormatException>(() => _ = Iou("1.2.3").ValueAsNumber);
            Assert.ThrowsExactly<FormatException>(() => _ = Iou(" 100 ").ValueAsNumber);
        }

        /// <summary>
        /// A non-finite string is not a quantity too large, and must not be reported as one.
        /// </summary>
        /// <remarks>
        /// <c>double.TryParse</c> accepts <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> whatever
        /// <c>NumberStyles</c> it is handed, because those symbols are matched separately from the
        /// numeric ones. Since the check that separates "will not fit" from "is not a number" runs
        /// through <c>double</c>, without a finiteness test these would come back as
        /// <see cref="AmountOutOfRangeException"/> - a confident answer about magnitude for a
        /// string that has none.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_NonFiniteStrings_AreFormatErrorsNotRangeErrors()
        {
            foreach (string value in new[] { "NaN", "Infinity", "-Infinity" })
            {
                Assert.ThrowsExactly<FormatException>(
                    () => _ = Iou(value).ValueAsNumber,
                    $"'{value}' is not a quantity at all, let alone one that is too large.");
            }
        }

        /// <summary>
        /// An out-of-range numerator is refused even when the denominator is zero.
        /// </summary>
        /// <remarks>
        /// <c>Offer.AmountEach</c> returns zero when <c>TakerPays</c> is zero. While it read the
        /// two sides lazily, that early return meant an unrepresentable <c>TakerGets</c> slipped
        /// through unnoticed - so whether the documented exception appeared depended on the value
        /// of an unrelated field, which is not a contract anyone can hold you to.
        /// </remarks>
        [TestMethod]
        public void AmountEach_OutOfRangeNumerator_ThrowsEvenWithAZeroDenominator()
        {
            Offer offer = new Offer { TakerGets = Iou("9e80"), TakerPays = Iou("0") };

            Assert.ThrowsExactly<AmountOutOfRangeException>(() => _ = offer.AmountEach);
        }

        /// <summary>
        /// A zero denominator on its own still yields zero rather than dividing.
        /// </summary>
        [TestMethod]
        public void AmountEach_ZeroDenominator_IsZero()
        {
            Offer offer = new Offer { TakerGets = Iou("100"), TakerPays = Iou("0") };

            Assert.AreEqual(0m, offer.AmountEach);
        }

        /// <summary>
        /// The two properties that compute rather than read fail the same single way.
        /// </summary>
        /// <remarks>
        /// <c>GetBalanceChanges</c> subtracts two balances and <c>Offer.AmountEach</c> divides two
        /// amounts, and both used to have a second failure behind the first: a clamped
        /// <c>decimal.MaxValue</c> would go on to throw <c>OverflowException</c> from the
        /// arithmetic, or - worse for the order book - return a plausible exchange rate that was
        /// wrong by 67 orders of magnitude without throwing at all.
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_ArithmeticOnOutOfRangeAmounts_FailsAtTheSource()
        {
            Assert.ThrowsExactly<AmountOutOfRangeException>(
                () => _ = Iou("9e80").ValueAsNumber - Iou("-100").ValueAsNumber,
                "This used to be an OverflowException from subtracting a clamped MaxValue.");

            Offer offer = new Offer { TakerGets = Iou("9e80"), TakerPays = Iou("1") };

            Assert.ThrowsExactly<AmountOutOfRangeException>(
                () => _ = offer.AmountEach,
                "This used to return decimal.MaxValue as an exchange rate, silently.");
        }

        /// <summary>
        /// <c>ToString</c> shows the amount instead of failing on it.
        /// </summary>
        /// <remarks>
        /// By convention <see cref="object.ToString"/> does not throw, and the places it is reached
        /// from - logging, string interpolation, a debugger's watch window - are exactly where
        /// someone would be while working out why an amount is unusual. Letting the getter throw
        /// through it would hide the value at the moment it is most wanted.
        /// </remarks>
        [TestMethod]
        public void ToString_OutOfRangeAmount_ShowsTheRawValue()
        {
            Assert.AreEqual("USD: 9e80", Iou("9e80").ToString());
            Assert.AreEqual("USD: -9e80", Iou("-9e80").ToString());
            Assert.AreEqual("USD: NaN", Iou("NaN").ToString());

            // Anything it can render, it still renders the same way.
            Assert.AreEqual("USD: 100", Iou("100").ToString());
        }

        /// <summary>
        /// Writing <see cref="decimal.MaxValue"/> produces an amount that cannot be read back.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Documented rather than fixed. The setter formats with <c>G16</c>, which rounds the
        /// mantissa to nearest - and near the ceiling that rounds <em>up</em>, past what
        /// <see cref="decimal"/> holds. So the SDK can write a string the ledger would accept
        /// (16-digit mantissa, exponent 13) and then refuse to read it.
        /// </para>
        /// <para>
        /// The window is the last ~7e12 below <see cref="decimal.MaxValue"/>, reachable only by
        /// assigning a number no token amount would be. Changing how the setter rounds would touch
        /// every round trip in the type to rescue a value nobody writes. Pinned here so the next
        /// person meets a decision rather than a surprise.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ValueAsNumber_WritingDecimalMaxValue_RoundsUpBeyondWhatCanBeRead()
        {
            Currency currency = new Currency { CurrencyCode = "USD", Issuer = "rIssuer" };
            currency.ValueAsNumber = decimal.MaxValue;

            Assert.AreEqual("7.922816251426434E+28", currency.Value);
            Assert.ThrowsExactly<AmountOutOfRangeException>(() => _ = currency.ValueAsNumber);

            // Just below the rounding boundary the round trip is intact, which is what makes the
            // line above an edge rather than a broken setter.
            currency.ValueAsNumber = 79228162514264330000000000000m;
            Assert.AreEqual(79228162514264330000000000000m, currency.ValueAsNumber);
        }

        #endregion

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
