using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.Client;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace Xrpl.X402;

/// <summary>
/// Default <see cref="IX402Signer"/> implementation that uses an in-memory <see cref="XrplWallet"/>
/// to autofill and locally sign an XRPL <see cref="Payment"/> transaction without submitting it.
/// </summary>
public sealed class XrplWalletX402Signer : IX402Signer
{
    private readonly IXrplClient _client;
    private readonly XrplWallet _wallet;

    /// <summary>
    /// Initializes a new instance of <see cref="XrplWalletX402Signer"/>.
    /// </summary>
    /// <param name="client">Connected XRPL client used to autofill transaction fields.</param>
    /// <param name="wallet">Wallet whose private key signs the transaction.</param>
    public XrplWalletX402Signer(IXrplClient client, XrplWallet wallet)
    {
        _client = client;
        _wallet = wallet;
    }

    /// <inheritdoc />
    public string PayerAddress => _wallet.ClassicAddress;

    /// <inheritdoc />
    public async Task<string> PrepareAndSignAsync(Payment payment, int? maxTimeoutSeconds = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> tx = payment.ToDictionary();
        Dictionary<string, object> autofilled = await _client.Autofill(tx, null, cancellationToken);

        if (maxTimeoutSeconds is int seconds && seconds > 0
            && autofilled.TryGetValue("LastLedgerSequence", out object? llsObj))
        {
            uint autofilledLls = Convert.ToUInt32(llsObj);
            uint current = await GetCurrentLedgerIndexAsync(cancellationToken);
            const double secondsPerLedger = 4.0;
            uint ledgersForTimeout = (uint)Math.Max(1, Math.Ceiling(seconds / secondsPerLedger));
            uint desired = current + ledgersForTimeout;
            uint capped = Math.Min(autofilledLls, desired);
            if (capped < current + 1) capped = current + 1; // keep at least one ledger of validity
            autofilled["LastLedgerSequence"] = capped;
        }

        SignatureResult sig = _wallet.Sign(autofilled);
        return sig.TxBlob;
    }

    private Task<uint> GetCurrentLedgerIndexAsync(CancellationToken cancellationToken)
    {
        return _client.GetLedgerIndex(cancellationToken);
    }
}
