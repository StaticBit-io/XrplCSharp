#nullable enable
using System;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// DynamicMPT (XLS-94): ImmutableFlags values shared by MPTokenIssuanceCreate
    /// and MPTokenIssuanceSet — which capabilities and fields are frozen for the
    /// lifetime of the issuance.
    ///
    /// A bit that is NOT set leaves the corresponding capability or field mutable,
    /// so an issuance created without ImmutableFlags can be changed later. Bits are
    /// only ever added: MPTokenIssuanceSet ORs the value into the ledger object
    /// (rippled MPTokenIssuanceSet::doApply), it never clears a bit.
    ///
    /// Values mirror rippled TxFlags.h tif* constants, which alias the lsif*
    /// ledger constants in LedgerFormats.h.
    /// </summary>
    [Flags]
    public enum MPTokenIssuanceImmutableFlags : uint
    {
        /// <summary>lsfMPTCanLock may never be enabled or disabled after this transaction.</summary>
        tifMPTCanLock = 0x00000002,

        /// <summary>lsfMPTRequireAuth may never be enabled or disabled after this transaction.</summary>
        tifMPTRequireAuth = 0x00000004,

        /// <summary>lsfMPTCanEscrow may never be enabled or disabled after this transaction.</summary>
        tifMPTCanEscrow = 0x00000008,

        /// <summary>lsfMPTCanTrade may never be enabled or disabled after this transaction.</summary>
        tifMPTCanTrade = 0x00000010,

        /// <summary>lsfMPTCanTransfer may never be enabled or disabled after this transaction.</summary>
        tifMPTCanTransfer = 0x00000020,

        /// <summary>lsfMPTCanClawback may never be enabled or disabled after this transaction.</summary>
        tifMPTCanClawback = 0x00000040,

        /// <summary>
        /// lsfMPTCanHoldConfidentialBalance may never be enabled after this transaction.
        /// Requires the ConfidentialTransfer amendment.
        /// </summary>
        tifMPTCanHoldConfidentialBalance = 0x00000080,

        /// <summary>MPTokenMetadata may never be changed after this transaction.</summary>
        tifMPTMetadata = 0x00010000,

        /// <summary>TransferFee may never be changed after this transaction.</summary>
        tifMPTTransferFee = 0x00020000,
    }
}
