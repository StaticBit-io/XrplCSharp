using System.Threading;
using System.Threading.Tasks;
using Xrpl.X402.Wire;

namespace Xrpl.X402.AspNetCore;

/// <summary>Verifies and settles an x402 PAYMENT-SIGNATURE on the XRP Ledger.</summary>
public interface IX402Facilitator
{
    /// <summary>Verify the signed payment matches the accepted requirement and settle it on-ledger.</summary>
    Task<PaymentResponseEnvelope> VerifyAndSettleAsync(PaymentSignatureEnvelope envelope, CancellationToken cancellationToken = default);
}
