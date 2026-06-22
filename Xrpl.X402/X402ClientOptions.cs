using System.Collections.Generic;

namespace Xrpl.X402;

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
}
