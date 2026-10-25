using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.X402.AspNetCore;

/// <summary>
/// Records the transactions a facilitator has settled, so that the same signed payment cannot
/// unlock a resource twice.
/// </summary>
/// <remarks>
/// <para>
/// A signed blob that has already been validated can be submitted again: the node answers
/// <c>tefPAST_SEQ</c> or <c>tefALREADY</c>, and waiting on its hash finds the original
/// <c>tesSUCCESS</c>. Without a record of what was settled, every presentation of the same
/// <c>PAYMENT-SIGNATURE</c> would look like a fresh payment.
/// </para>
/// <para>
/// A hash must be kept until a ledger past its <c>LastLedgerSequence</c> is validated; after that
/// <see cref="LedgerSettlingFacilitator"/> refuses the blob as expired before it reaches the store.
/// An implementation backed by a time-based TTL must make the TTL cover that many ledgers.
/// </para>
/// <para>
/// The default <see cref="InMemorySettledPaymentStore"/> lives in one process. A deployment that
/// runs several instances behind a load balancer must share one store between them (a database or
/// a distributed cache with an atomic insert-if-absent), or a payment settled by one instance can be
/// replayed against another.
/// </para>
/// <para>
/// A store is keyed by ledger index, so it serves one network: instances settling on mainnet and
/// on testnet need separate stores, or the higher ledger indexes of one network prune the claims of
/// the other while their windows are open. Instances sharing a store should read the validated
/// ledger from nodes in step: an <see cref="InMemorySettledPaymentStore"/>-style pruning by
/// <c>validatedLedgerIndex</c> can drop a claim while an instance on a node lagging by more than
/// the retention margin still accepts the blob as unexpired.
/// </para>
/// </remarks>
public interface ISettledPaymentStore
{
    /// <summary>
    /// Atomically claims <paramref name="transactionHash"/> for settlement.
    /// </summary>
    /// <param name="transactionHash">Hash of the signed transaction, uppercase hex.</param>
    /// <param name="lastLedgerSequence">
    /// The transaction's <c>LastLedgerSequence</c>: the claim must be kept at least until a later
    /// ledger is validated.
    /// </param>
    /// <param name="validatedLedgerIndex">
    /// The latest validated ledger the facilitator saw; claims whose window ended well before it may
    /// be dropped.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <c>true</c> when the hash was not claimed and now is; <c>false</c> when it is already claimed,
    /// whether settled or still being settled.
    /// </returns>
    /// <remarks>
    /// An implementation that throws must leave the hash unclaimed. The facilitator cannot release a
    /// claim it was not told it holds - releasing it could drop a concurrent caller's claim and admit
    /// the blob twice - so a claim written before an exception (a cancelled request, a reply that
    /// timed out) refuses the blob as <c>duplicate_payment</c> until the store prunes it.
    /// </remarks>
    ValueTask<bool> TryReserveAsync(
        string transactionHash,
        uint lastLedgerSequence,
        uint validatedLedgerIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a claim whose settlement did not succeed, so the payer can present the same blob again.
    /// </summary>
    /// <param name="transactionHash">Hash passed to <see cref="TryReserveAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask ReleaseAsync(string transactionHash, CancellationToken cancellationToken = default);
}
