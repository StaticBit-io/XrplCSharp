namespace Xrpl.Models
{
    /// <summary>
    /// A granular permission: the right to perform part of a transaction type rather than all of
    /// it, granted to a delegate through <c>DelegateSet</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors rippled's <c>GranularPermissionType</c>, declared in
    /// <c>include/xrpl/protocol/detail/permissions.macro</c>. The values start above
    /// <see cref="ushort.MaxValue"/> by design, so that they can never collide with a
    /// transaction-type permission, which is the transaction type code plus one.
    /// <para>
    /// A granular permission may be delegated even when the transaction it belongs to may not:
    /// <see cref="AccountDomainSet"/> is grantable although <c>AccountSet</c> as a whole is not.
    /// </para>
    /// <para>
    /// Unlike the other protocol enumerations, this one is not generated - <c>definitions.json</c>
    /// does not carry granular permissions. It is held against the vendored macro by
    /// <c>TestUGranularPermissionConformance</c>.
    /// </para>
    /// </remarks>
    public enum GranularPermission : uint
    {
        /// <summary>Authorize a trustline (<c>TrustSet</c>, <c>LimitAmount</c>).</summary>
        TrustlineAuthorize = 65537,

        /// <summary>Freeze a trustline (<c>TrustSet</c>).</summary>
        TrustlineFreeze = 65538,

        /// <summary>Unfreeze a trustline (<c>TrustSet</c>).</summary>
        TrustlineUnfreeze = 65539,

        /// <summary>Set the account's domain (<c>AccountSet</c>).</summary>
        AccountDomainSet = 65540,

        /// <summary>Set the account's email hash (<c>AccountSet</c>).</summary>
        AccountEmailHashSet = 65541,

        /// <summary>Set the account's message key (<c>AccountSet</c>).</summary>
        AccountMessageKeySet = 65542,

        /// <summary>Set the account's transfer rate (<c>AccountSet</c>).</summary>
        AccountTransferRateSet = 65543,

        /// <summary>Set the account's tick size (<c>AccountSet</c>).</summary>
        AccountTickSizeSet = 65544,

        /// <summary>Issue tokens through a payment from the issuer (<c>Payment</c>).</summary>
        PaymentMint = 65545,

        /// <summary>Redeem tokens through a payment back to the issuer (<c>Payment</c>).</summary>
        PaymentBurn = 65546,

        /// <summary>Lock an MPT issuance (<c>MPTokenIssuanceSet</c>).</summary>
        MPTokenIssuanceLock = 65547,

        /// <summary>Unlock an MPT issuance (<c>MPTokenIssuanceSet</c>).</summary>
        MPTokenIssuanceUnlock = 65548,
    }
}
