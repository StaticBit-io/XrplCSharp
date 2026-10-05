using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;
using Xrpl.Sugar;
using Xrpl.X402.Wire;

namespace Xrpl.X402.AspNetCore;

/// <summary>
/// Production-quality x402 facilitator that decodes a signed XRPL transaction blob,
/// validates the destination address, submits the transaction to the ledger, and waits
/// for a validated outcome.
/// </summary>
/// <remarks>
/// A settlement whose outcome the client could not tell - the submit request lost on a
/// reconnect, a lookup that failed while waiting - is waited for once more before it is
/// refused, since the payer's funds may already have moved. If it still cannot be told, the
/// response is <c>settlement_unknown</c> and carries the transaction hash, so the payment can be
/// reconciled rather than lost.
/// </remarks>
public sealed class LedgerSettlingFacilitator : IX402Facilitator
{
    private readonly Func<string, CancellationToken, Task<TransactionSummary>> _submit;
    private readonly Func<TransactionOutcomeUnknownException, CancellationToken, Task<TransactionSummary>> _resolve;

    /// <summary>
    /// Initializes a new instance of <see cref="LedgerSettlingFacilitator"/>.
    /// </summary>
    /// <param name="client">Connected XRPL client used to submit transactions.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
    public LedgerSettlingFacilitator(IXrplClient client)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        _submit = (blob, cancellationToken) => client.SubmitRequestAndWait(blob, failHard: false, cancellationToken);
        _resolve = (unknown, cancellationToken) => client.WaitForTransactionOutcome(
            unknown.Hash, unknown.LastLedgerSequence, unknown.MinLedger, cancellationToken);
    }

    /// <summary>Settles through <paramref name="submit"/> and resolves an unknown outcome through <paramref name="resolve"/>.</summary>
    internal LedgerSettlingFacilitator(
        Func<string, CancellationToken, Task<TransactionSummary>> submit,
        Func<TransactionOutcomeUnknownException, CancellationToken, Task<TransactionSummary>> resolve)
    {
        _submit = submit;
        _resolve = resolve;
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
            TransactionSummary summary;
            try
            {
                summary = await _submit(signedBlob, cancellationToken);
            }
            catch (TransactionOutcomeUnknownException unknown)
            {
                try
                {
                    summary = await _resolve(unknown, cancellationToken);
                }
                catch (TransactionOutcomeUnknownException stillUnknown)
                {
                    return new PaymentResponseEnvelope
                    {
                        Success = false,
                        ErrorReason = "settlement_unknown",
                        Transaction = stillUnknown.Hash,
                        Network = envelope.Accepted.Network,
                        Payer = payer
                    };
                }
            }

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TransactionFailedException or TransactionExpiredException)
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "settlement_failed"
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
