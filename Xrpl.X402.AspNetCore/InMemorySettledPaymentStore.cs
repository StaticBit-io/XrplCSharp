using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.X402.AspNetCore;

/// <summary>
/// Process-local <see cref="ISettledPaymentStore"/>. Suitable for a single instance only: several
/// instances behind a load balancer need a shared store.
/// </summary>
/// <remarks>
/// A claim is kept until the validated ledger passes its <c>LastLedgerSequence</c> by
/// <see cref="RetentionMarginLedgers"/>. The margin covers a request that read the validated ledger
/// before another request advanced it and pruned: that request still checks the blob's expiry
/// against its older ledger, and must still find the claim.
/// </remarks>
public sealed class InMemorySettledPaymentStore : ISettledPaymentStore
{
    /// <summary>Default <see cref="RetentionMarginLedgers"/>: about a quarter of an hour on mainnet.</summary>
    public const uint DefaultRetentionMarginLedgers = 256;

    private readonly ConcurrentDictionary<string, uint> _claims = new(StringComparer.OrdinalIgnoreCase);
    private long _prunedAt;

    /// <summary>Initializes a new instance of <see cref="InMemorySettledPaymentStore"/>.</summary>
    /// <param name="retentionMarginLedgers">
    /// Ledgers a claim is kept past its <c>LastLedgerSequence</c>.
    /// </param>
    public InMemorySettledPaymentStore(uint retentionMarginLedgers = DefaultRetentionMarginLedgers)
    {
        RetentionMarginLedgers = retentionMarginLedgers;
    }

    /// <summary>Ledgers a claim is kept past its <c>LastLedgerSequence</c>.</summary>
    public uint RetentionMarginLedgers { get; }

    /// <summary>Number of claims currently held.</summary>
    public int Count => _claims.Count;

    /// <inheritdoc />
    public ValueTask<bool> TryReserveAsync(
        string transactionHash,
        uint lastLedgerSequence,
        uint validatedLedgerIndex,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(transactionHash))
            throw new ArgumentException("A transaction hash is required.", nameof(transactionHash));

        Prune(validatedLedgerIndex);
        return new ValueTask<bool>(_claims.TryAdd(transactionHash, lastLedgerSequence));
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string transactionHash, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(transactionHash))
            _claims.TryRemove(transactionHash, out _);

        return default;
    }

    // Scans at most once per validated ledger: requests within one ledger would only find nothing new.
    private void Prune(uint validatedLedgerIndex)
    {
        long previous = Interlocked.Read(ref _prunedAt);
        if (validatedLedgerIndex <= previous
            || Interlocked.CompareExchange(ref _prunedAt, validatedLedgerIndex, previous) != previous)
        {
            return;
        }

        foreach (KeyValuePair<string, uint> claim in _claims)
        {
            if ((ulong)claim.Value + RetentionMarginLedgers < validatedLedgerIndex)
                _claims.TryRemove(claim);
        }
    }
}
