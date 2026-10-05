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

    /// <summary>Transaction hash (tx_id): validated on success, or the one to reconcile on <c>settlement_unknown</c>; <c>null</c> on other failures.</summary>
    [JsonPropertyName("transaction")] public string? Transaction { get; set; }

    /// <summary>CAIP-2 network identifier of the transaction, on success or <c>settlement_unknown</c>; <c>null</c> on other failures.</summary>
    [JsonPropertyName("network")] public string? Network { get; set; }

    /// <summary>Classic XRPL address of the payer account, on success or <c>settlement_unknown</c>; <c>null</c> on other failures.</summary>
    [JsonPropertyName("payer")] public string? Payer { get; set; }

    /// <summary>Short machine-readable error code on failure (e.g. <c>"settlement_failed"</c>); <c>null</c> on success.</summary>
    [JsonPropertyName("errorReason")] public string? ErrorReason { get; set; }
}
