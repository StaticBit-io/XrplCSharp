using System.Threading;
using System.Threading.Tasks;
using Xrpl.Models.Transactions;

namespace Xrpl.X402;

public interface IX402Signer
{
    string PayerAddress { get; }

    /// <summary>
    /// Autofill + locally sign the payment. MUST NOT submit. Returns signed tx_blob (hex).
    /// When <paramref name="maxTimeoutSeconds"/> is provided, the signed transaction's
    /// <c>LastLedgerSequence</c> is capped so the payment expires no later than that many
    /// seconds from now (never extended beyond the autofill default).
    /// </summary>
    Task<string> PrepareAndSignAsync(Payment payment, int? maxTimeoutSeconds = null, CancellationToken cancellationToken = default);
}
