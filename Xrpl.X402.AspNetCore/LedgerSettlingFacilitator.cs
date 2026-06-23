using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Sugar;
using Xrpl.X402.Wire;

namespace Xrpl.X402.AspNetCore;

/// <summary>
/// Production-quality x402 facilitator that decodes a signed XRPL transaction blob,
/// validates the destination address, submits the transaction to the ledger, and waits
/// for a validated outcome.
/// </summary>
public sealed class LedgerSettlingFacilitator : IX402Facilitator
{
    private readonly IXrplClient _client;

    /// <summary>
    /// Initializes a new instance of <see cref="LedgerSettlingFacilitator"/>.
    /// </summary>
    /// <param name="client">Connected XRPL client used to submit transactions.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
    public LedgerSettlingFacilitator(IXrplClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<PaymentResponseEnvelope> VerifyAndSettleAsync(
        PaymentSignatureEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        string signedBlob = envelope.Payload.SignedTxBlob;

        // Decode the signed transaction blob to read Account (payer) and Destination
        string decodedJson = XrplBinaryCodec.Decode(signedBlob).ToString();
        using JsonDocument doc = JsonDocument.Parse(decodedJson);
        JsonElement root = doc.RootElement;

        string payer = root.TryGetProperty("Account", out JsonElement accountEl)
            ? accountEl.GetString() ?? string.Empty
            : string.Empty;

        string destination = root.TryGetProperty("Destination", out JsonElement destEl)
            ? destEl.GetString() ?? string.Empty
            : string.Empty;

        // Verify the transaction destination matches the accepted pay-to address
        if (!string.Equals(destination, envelope.Accepted.PayTo, StringComparison.Ordinal))
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "invalid_destination"
            };
        }

        // Submit the signed transaction and wait for validated outcome
        try
        {
            Xrpl.Models.Methods.TransactionSummary summary =
                await _client.SubmitRequestAndWait(signedBlob, failHard: false, cancellationToken);

            string? txResult = summary.Meta?.TransactionResult;
            bool succeeded = summary.Validated
                && txResult is string r
                && r.StartsWith("tes", StringComparison.Ordinal);

            if (!succeeded)
            {
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = "settlement_failed"
                };
            }

            return new PaymentResponseEnvelope
            {
                Success = true,
                Transaction = summary.Hash,
                Network = envelope.Accepted.Network,
                Payer = payer
            };
        }
        catch
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "settlement_error"
            };
        }
    }
}
