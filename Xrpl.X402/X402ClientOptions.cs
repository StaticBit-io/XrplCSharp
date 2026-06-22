using System.Collections.Generic;

namespace Xrpl.X402;

/// <summary>
/// Configuration options for the x402 payment client (<see cref="X402PaymentHandler"/>).
/// Controls the target network, amount caps, address allowlists, and field name mappings.
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

    /// <summary>Key in the requirement's <c>extra</c> object to read the payment id from (t54 uses "invoiceId").</summary>
    public string InvoiceIdExtraKey { get; set; } = "invoiceId";

    /// <summary>Key in the requirement's <c>extra</c> object to read the optional session id from.</summary>
    public string SessionIdExtraKey { get; set; } = "sessionId";

    /// <summary>JSON field name for the payment id inside the x402 memo (mpcp adapter uses "paymentId").</summary>
    public string MemoPaymentIdField { get; set; } = "paymentId";

    /// <summary>JSON field name for the optional session id inside the x402 memo.</summary>
    public string MemoSessionIdField { get; set; } = "sessionId";

    /// <summary>Optional Verifiable Intent provider. When set, its extensions object is attached to each PAYMENT-SIGNATURE. Null = no VI.</summary>
    public IVerifiableIntentProvider? VerifiableIntentProvider { get; set; }
}
