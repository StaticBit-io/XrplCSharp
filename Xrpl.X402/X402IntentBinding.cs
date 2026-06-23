namespace Xrpl.X402;

/// <summary>How the x402 payment id is bound to the XRPL transaction.</summary>
public enum X402IntentBinding
{
    /// <summary>
    /// Bind via the native XRPL Payment InvoiceID field (t54 / standard XRPL exact scheme).
    /// Requires a 64-hex invoiceId. This is the default and the t54-correct mode.
    /// </summary>
    InvoiceIdField,

    /// <summary>
    /// Bind via an XRPL Memo carrying {paymentId} JSON (mpcp-style facilitators).
    /// </summary>
    Memo,
}
