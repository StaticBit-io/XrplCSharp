using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.Client;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace Xrpl.X402;

public sealed class XrplWalletX402Signer : IX402Signer
{
    private readonly IXrplClient _client;
    private readonly XrplWallet _wallet;

    public XrplWalletX402Signer(IXrplClient client, XrplWallet wallet)
    {
        _client = client;
        _wallet = wallet;
    }

    public string PayerAddress => _wallet.ClassicAddress;

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
