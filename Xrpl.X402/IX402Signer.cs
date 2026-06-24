using System.Threading;
using System.Threading.Tasks;
using Xrpl.Models.Transactions;

namespace Xrpl.X402;

/// <summary>
/// Abstracts the signing of an XRPL <see cref="Payment"/> transaction for x402 payments.
/// Implementations autofill transaction fields and sign locally without submitting to the ledger.
/// </summary>
public interface IX402Signer
{
    /// <summary>Classic XRPL address of the account that signs and funds payments.</summary>
    string PayerAddress { get; }

    /// <summary>
    /// Autofill + locally sign the payment. MUST NOT submit. Returns signed tx_blob (hex).
    /// When <paramref name="maxTimeoutSeconds"/> is provided, the signed transaction's
    /// <c>LastLedgerSequence</c> is capped so the payment expires no later than that many
    /// seconds from now (never extended beyond the autofill default).
    /// </summary>
    Task<string> PrepareAndSignAsync(Payment payment, int? maxTimeoutSeconds = null, CancellationToken cancellationToken = default);
}
