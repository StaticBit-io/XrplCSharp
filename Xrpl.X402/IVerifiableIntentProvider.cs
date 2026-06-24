using System.Threading;
using System.Threading.Tasks;
using Xrpl.Models.Transactions;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

/// <summary>
/// Optional hook that produces an x402 <c>extensions</c> object (e.g. a Verifiable Intent
/// SD-JWT chain under <c>x402Secure.verifiableIntentChain</c>) to attach to a PAYMENT-SIGNATURE.
/// Implementations integrate with a credential provider (e.g. Trustline); this package ships no default.
/// </summary>
public interface IVerifiableIntentProvider
{
    /// <summary>Build the extensions object for this payment, or return null to attach nothing.</summary>
    Task<object?> CreateExtensionsAsync(PaymentRequirement requirement, Payment payment, CancellationToken cancellationToken = default);
}
