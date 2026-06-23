namespace Xrpl.X402;

/// <summary>How the x402 invoice id is bound to the XRPL transaction.</summary>
public enum X402IntentBinding
{
    /// <summary>
    /// Bind via the native XRPL Payment InvoiceID field only (SHA-256 of the raw invoice id).
    /// </summary>
    InvoiceIdField,

    /// <summary>
    /// Bind via an XRPL Memo only: MemoData = UTF-8 hex of the raw invoice id string.
    /// No MemoType or MemoFormat — exactly as the t54 reference payer emits.
    /// </summary>
    Memo,

    /// <summary>
    /// Set both the InvoiceID field (SHA-256) and a Memo (UTF-8 hex).
    /// This is the default — matches the t54 reference payer default (<c>invoice_binding = "both"</c>).
    /// </summary>
    Both,
}
