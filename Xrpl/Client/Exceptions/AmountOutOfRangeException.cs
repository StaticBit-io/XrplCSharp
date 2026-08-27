using System;
using System.Globalization;

namespace Xrpl.Client.Exceptions
{
    /// <summary>
    /// An issued-currency amount the node sent is outside the range <see cref="decimal"/> can hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XRPL issued currency runs from <c>1e-81</c> to roughly <c>1e96</c> - rippled's
    /// <c>STAmount</c> allows a 16-digit mantissa with an exponent in <c>[-96, 80]</c> - while
    /// <see cref="decimal"/> stops at about <c>7.9e28</c>. The two do not fit inside one another,
    /// and no parsing of the string can change that.
    /// </para>
    /// <para>
    /// This used to be answered by clamping to <see cref="decimal.MaxValue"/>, which is a number
    /// that is wrong by up to 67 orders of magnitude and does not say so - and by a bare
    /// <see cref="FormatException"/> for negative amounts, which named the string rather than the
    /// problem. Both are gone; the amount that does not fit is reported as such.
    /// </para>
    /// <para>
    /// <see cref="Value"/> carries what the node actually sent, so a caller who needs the real
    /// figure still has it. Representing it rather than reporting it is issue #150.
    /// </para>
    /// </remarks>
    public class AmountOutOfRangeException : RippleException
    {
        /// <summary>
        /// The amount as the node sent it, in the ledger's own string form.
        /// </summary>
        public string Value { get; }

        /// <inheritdoc cref="AmountOutOfRangeException"/>
        public AmountOutOfRangeException(string value)
            : base(BuildMessage(value))
        {
            Value = value;
        }

        private static string BuildMessage(string value) => string.Format(
            CultureInfo.InvariantCulture,
            "The amount '{0}' is outside the range System.Decimal can represent (about ±7.9e28). " +
            "XRPL issued currency reaches roughly 1e96, so this is a legitimate ledger value that " +
            "this property cannot return. Read Currency.Value for the amount as sent.",
            value);
    }
}
