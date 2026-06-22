using System.Threading;
using System.Threading.Tasks;
using Xrpl.Models.Transactions;

namespace Xrpl.X402;

public interface IX402Signer
{
    string PayerAddress { get; }

    /// <summary>Autofill + locally sign the payment. MUST NOT submit. Returns signed tx_blob (hex).</summary>
    Task<string> PrepareAndSignAsync(Payment payment, CancellationToken cancellationToken = default);
}
