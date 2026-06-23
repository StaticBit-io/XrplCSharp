using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Sugar;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Integration;

/// <summary>
/// In-test x402 facilitator: decodes a signed blob, validates destination,
/// submits it to the connected ledger, and waits for a final validated outcome.
/// </summary>
public sealed class TestFacilitator
{
    private readonly IXrplClient _client;

    public TestFacilitator(IXrplClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<PaymentResponseEnvelope> VerifyAndSettleAsync(
        PaymentSignatureEnvelope env,
        CancellationToken cancellationToken = default)
    {
        string signedBlob = env.Payload.SignedTxBlob;

        // Decode blob to read Account + Destination
        string decodedJson = XrplBinaryCodec.Decode(signedBlob).ToString();
        using JsonDocument doc = JsonDocument.Parse(decodedJson);
        JsonElement root = doc.RootElement;

        string payer = root.TryGetProperty("Account", out JsonElement accountEl)
            ? accountEl.GetString() ?? string.Empty
            : string.Empty;

        string destination = root.TryGetProperty("Destination", out JsonElement destEl)
            ? destEl.GetString() ?? string.Empty
            : string.Empty;

        // Verify destination matches the accepted pay-to address
        if (!string.Equals(destination, env.Accepted.PayTo, StringComparison.Ordinal))
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = "invalid_destination"
            };
        }

        // Submit and wait for validated outcome
        try
        {
            Xrpl.Models.Methods.TransactionSummary summary =
                await _client.SubmitRequestAndWait(signedBlob, failHard: false, cancellationToken);

            // success when validated and result starts with "tes"
            string? txResult = summary.Meta?.TransactionResult;
            bool succeeded = summary.Validated
                && txResult != null
                && txResult.StartsWith("tes", StringComparison.Ordinal);

            if (!succeeded)
            {
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = $"settlement_failed:{txResult ?? "not_validated"}"
                };
            }

            return new PaymentResponseEnvelope
            {
                Success = true,
                Transaction = summary.Hash,
                Network = env.Accepted.Network,
                Payer = payer
            };
        }
        catch (Exception ex)
        {
            return new PaymentResponseEnvelope
            {
                Success = false,
                ErrorReason = $"settlement_failed:{ex.Message}"
            };
        }
    }
}
