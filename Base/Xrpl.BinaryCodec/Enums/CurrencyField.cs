namespace Xrpl.BinaryCodec.Enums
{
    /// <summary>
    /// Represents a Currency field used for Oracle price data (BaseAsset, QuoteAsset).
    /// </summary>
    public class CurrencyField : Field
    {
        /// <summary>
        /// Initializes a new instance of the CurrencyField class.
        /// </summary>
        /// <param name="name">Field name.</param>
        /// <param name="nthOfType">Ordinal position within the type.</param>
        /// <param name="isSigningField">Whether this field is included in signing.</param>
        public CurrencyField(string name, int nthOfType, bool isSigningField = true) :
            base(name, nthOfType, FieldType.Currency, isSigningField)
        {
        }
    }
}
