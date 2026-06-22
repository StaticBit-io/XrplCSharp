using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

/// <summary>
/// The settlement result returned by the facilitator in the <c>PAYMENT-RESPONSE</c> header (t54 exact scheme).
/// Indicates whether the payment was successfully validated on-ledger.
/// </summary>
public sealed class PaymentResponseEnvelope
{
    /// <summary><c>true</c> if the payment was validated on the ledger; <c>false</c> on any failure.</summary>
    [JsonPropertyName("success")] public bool Success { get; set; }

    /// <summary>Validated transaction hash (tx_id) on success; <c>null</c> on failure.</summary>
    [JsonPropertyName("transaction")] public string? Transaction { get; set; }

    /// <summary>CAIP-2 network identifier on which the transaction was validated; <c>null</c> on failure.</summary>
    [JsonPropertyName("network")] public string? Network { get; set; }

    /// <summary>Classic XRPL address of the payer account on success; <c>null</c> on failure.</summary>
    [JsonPropertyName("payer")] public string? Payer { get; set; }

    /// <summary>Short machine-readable error code on failure (e.g. <c>"settlement_failed"</c>); <c>null</c> on success.</summary>
    [JsonPropertyName("errorReason")] public string? ErrorReason { get; set; }
}
