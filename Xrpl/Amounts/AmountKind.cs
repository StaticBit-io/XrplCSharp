namespace Xrpl.Amounts
{
    /// <summary>What an amount holds, which decides how rippled's <c>STAmount</c> rounds it.</summary>
    public enum AmountKind
    {
        /// <summary>XRP, a whole number of drops.</summary>
        Xrp,

        /// <summary>An issued currency: 16 significant digits, exponent -96 to 80.</summary>
        Iou,

        /// <summary>A multi-purpose token, a whole number of units.</summary>
        Mpt,
    }
}
