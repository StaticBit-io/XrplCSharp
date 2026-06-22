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

    public async Task<string> PrepareAndSignAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> txDict = payment.ToDictionary();
        (string txBlob, _) = await _client.GetSignedTx(txDict, autofill: true, failHard: false,
            wallet: _wallet, cancellationToken: cancellationToken);
        return txBlob;
    }
}
