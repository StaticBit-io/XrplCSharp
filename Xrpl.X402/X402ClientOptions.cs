using System.Collections.Generic;

namespace Xrpl.X402;

/// <summary>
/// Configuration options for the x402 payment client (<see cref="X402PaymentHandler"/>).
/// Controls the target network, amount caps, address allowlists, and binding mode.
/// </summary>
public sealed class X402ClientOptions
{
    /// <summary>CAIP-2 network the client is willing to pay on, e.g. "xrpl:1".</summary>
    public string Network { get; set; } = "xrpl:1";

    /// <summary>Hard cap for XRP payments, in drops. Requirement above this is refused.</summary>
    public ulong MaxAmountDrops { get; set; } = 10_000_000; // 10 XRP

    /// <summary>Optional per-issuer value caps for IOU/RLUSD (key = issuer address).</summary>
    public Dictionary<string, decimal> IouValueCaps { get; set; } = new();

    /// <summary>Optional allowlist of acceptable payTo / issuer addresses. Empty = allow any.</summary>
    public HashSet<string> PayToAllowlist { get; set; } = new();

    /// <summary>x402 protocol version emitted in PAYMENT-SIGNATURE.</summary>
    public int X402Version { get; set; } = 2;

    /// <summary>Key in the requirement's <c>extra</c> object to read the invoice id from (t54 uses "invoiceId").</summary>
    public string InvoiceIdExtraKey { get; set; } = "invoiceId";

    /// <summary>Optional Verifiable Intent provider. When set, its extensions object is attached to each PAYMENT-SIGNATURE. Null = no VI.</summary>
    public IVerifiableIntentProvider? VerifiableIntentProvider { get; set; }

    /// <summary>
    /// How the x402 invoice id is bound to the XRPL transaction.
    /// <para>
    /// <see cref="X402IntentBinding.Both"/> (default) sets both <c>Payment.InvoiceID</c> (SHA-256 of the raw invoice id)
    /// and a Memo with <c>MemoData = UTF-8 hex of the raw invoice id</c>.
    /// This matches the t54 reference payer default (<c>invoice_binding = "both"</c>).
    /// </para>
    /// <para>
    /// <see cref="X402IntentBinding.InvoiceIdField"/> sets only <c>Payment.InvoiceID</c>.
    /// <see cref="X402IntentBinding.Memo"/> sets only the Memo.
    /// </para>
    /// </summary>
    public X402IntentBinding IntentBinding { get; set; } = X402IntentBinding.Both;
}
