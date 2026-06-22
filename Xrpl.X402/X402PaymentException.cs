using System;

namespace Xrpl.X402;

/// <summary>
/// Exception thrown by the x402 payment client when a payment cannot be negotiated or executed.
/// Carries a machine-readable <see cref="Reason"/> code alongside the human-readable message.
/// </summary>
public sealed class X402PaymentException : Exception
{
    /// <summary>Short machine-readable error code (e.g. <c>"amount_over_cap"</c>, <c>"payto_not_allowed"</c>).</summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="X402PaymentException"/>.
    /// </summary>
    /// <param name="reason">Short machine-readable reason code.</param>
    /// <param name="message">Human-readable description of the error.</param>
    public X402PaymentException(string reason, string message) : base(message) => Reason = reason;
}
