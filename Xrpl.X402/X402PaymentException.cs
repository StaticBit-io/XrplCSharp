using System;

namespace Xrpl.X402;

public sealed class X402PaymentException : Exception
{
    public string Reason { get; }
    public X402PaymentException(string reason, string message) : base(message) => Reason = reason;
}
