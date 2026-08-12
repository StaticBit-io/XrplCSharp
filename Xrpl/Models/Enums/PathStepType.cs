using System;

//https://xrpl.org/paths.html#path-steps
namespace Xrpl.Models.Enums
{
    /// <summary>
    /// Bitmask describing which fields a path step carries, as reported by rippled in the
    /// <c>type</c> field of every path step (STPathElement upstream).<br/>
    /// The value is derived from the fields actually present in the step: rippled ignores it when
    /// parsing a submitted transaction, and the binary codec synthesizes the byte itself.<br/>
    /// A value carrying a bit this enum does not declare is preserved as-is on deserialization.
    /// </summary>
    [Flags]
    public enum PathStepType : uint
    {
        /// <summary> No field present. </summary>
        None = 0x00,
        /// <summary> Rippling through an account (as opposed to taking an offer). </summary>
        Account = 0x01,
        /// <summary> A currency is present, changing the asset through an order book. </summary>
        Currency = 0x10,
        /// <summary> An issuer is present. </summary>
        Issuer = 0x20,
        /// <summary> An MPTokenIssuanceID is present (rippled 3.2.0+, MPTokensV2 amendment). </summary>
        MPTokenIssuanceID = 0x40,
    }
}
