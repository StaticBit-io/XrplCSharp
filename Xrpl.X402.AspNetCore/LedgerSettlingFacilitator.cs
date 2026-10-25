using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils.Hashes;
using Xrpl.X402.Wire;

namespace Xrpl.X402.AspNetCore;

/// <summary>
/// Production-quality x402 facilitator that decodes a signed XRPL transaction blob, checks it
/// against the accepted requirement, submits it to the ledger, waits for a validated outcome and
/// checks what the ledger actually delivered.
/// </summary>
/// <remarks>
/// <para>
/// Before submitting, the blob must be a <c>Payment</c> to <see cref="PaymentRequirement.PayTo"/>
/// of exactly <see cref="PaymentRequirement.Amount"/> in <see cref="PaymentRequirement.Asset"/>
/// (and <c>extra.issuer</c> for an issued currency), without <c>tfPartialPayment</c>, with a
/// <c>LastLedgerSequence</c> that has not passed. After validation, the metadata's
/// <c>delivered_amount</c> must cover the requirement. Each refusal has its own
/// <see cref="PaymentResponseEnvelope.ErrorReason"/>: <c>invalid_transaction_type</c>,
/// <c>invalid_destination</c>, <c>invalid_partial_payment</c>, <c>invalid_asset</c>,
/// <c>invalid_amount</c>, <c>invalid_last_ledger_sequence</c>, <c>payment_expired</c>,
/// <c>duplicate_payment</c> and <c>delivered_amount_mismatch</c>.
/// </para>
/// <para>
/// Each settled hash is recorded in an <see cref="ISettledPaymentStore"/>, and a blob already
/// recorded is refused as <c>duplicate_payment</c>: resubmitting a validated blob makes the node
/// report the original <c>tesSUCCESS</c>, which would otherwise unlock the resource again. The
/// store must be shared by every instance that settles for the same merchant; see
/// <see cref="ISettledPaymentStore"/>.
/// </para>
/// <para>
/// A settlement whose outcome the client could not tell - the submit request lost on a
/// reconnect, a lookup that failed while waiting - is waited for once more before it is
/// refused, since the payer's funds may already have moved. If it still cannot be told, the
/// response is <c>settlement_unknown</c> and carries the transaction hash, so the payment can be
/// reconciled rather than lost.
/// </para>
/// </remarks>
public sealed class LedgerSettlingFacilitator : IX402Facilitator
{
    private const uint PartialPaymentFlag = (uint)PaymentFlags.tfPartialPayment;

    private const NumberStyles DropsStyle = NumberStyles.None;

    // The ledger's amount strings: a decimal point and an exponent, never a sign in a payment.
    private const NumberStyles TokenValueStyle = NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private readonly Func<string, CancellationToken, Task<TransactionSummary>> _submit;
    private readonly Func<TransactionOutcomeUnknownException, CancellationToken, Task<TransactionSummary>> _resolve;
    private readonly Func<CancellationToken, Task<uint>> _validatedLedgerIndex;
    private readonly ISettledPaymentStore _settled;

    /// <summary>
    /// Initializes a new instance of <see cref="LedgerSettlingFacilitator"/> with its own
    /// <see cref="InMemorySettledPaymentStore"/>.
    /// </summary>
    /// <remarks>
    /// Replays are caught only by the same instance: keep one facilitator for the application, and
    /// use the overload taking an <see cref="ISettledPaymentStore"/> when several processes settle.
    /// </remarks>
    /// <param name="client">Connected XRPL client used to submit transactions.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
    public LedgerSettlingFacilitator(IXrplClient client)
        : this(client, new InMemorySettledPaymentStore())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="LedgerSettlingFacilitator"/> that records settled
    /// payments in <paramref name="settledPayments"/>.
    /// </summary>
    /// <param name="client">Connected XRPL client used to submit transactions.</param>
    /// <param name="settledPayments">Store of settled transaction hashes, shared by every instance that settles.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public LedgerSettlingFacilitator(IXrplClient client, ISettledPaymentStore settledPayments)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        _submit = (blob, cancellationToken) => client.SubmitRequestAndWait(blob, failHard: false, cancellationToken);
        _resolve = (unknown, cancellationToken) => client.WaitForTransactionOutcome(
            unknown.Hash, unknown.LastLedgerSequence, unknown.MinLedger, cancellationToken);
        _validatedLedgerIndex = cancellationToken => client.GetLedgerIndex(cancellationToken);
        _settled = settledPayments ?? throw new ArgumentNullException(nameof(settledPayments));
    }

    /// <summary>
    /// Settles through <paramref name="submit"/>, resolves an unknown outcome through
    /// <paramref name="resolve"/> and reads the validated ledger through <paramref name="validatedLedgerIndex"/>.
    /// </summary>
    internal LedgerSettlingFacilitator(
        Func<string, CancellationToken, Task<TransactionSummary>> submit,
        Func<TransactionOutcomeUnknownException, CancellationToken, Task<TransactionSummary>> resolve,
        Func<CancellationToken, Task<uint>> validatedLedgerIndex,
        ISettledPaymentStore settledPayments)
    {
        _submit = submit;
        _resolve = resolve;
        _validatedLedgerIndex = validatedLedgerIndex;
        _settled = settledPayments;
    }

    /// <inheritdoc />
    public async Task<PaymentResponseEnvelope> VerifyAndSettleAsync(
        PaymentSignatureEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        string signedBlob = envelope.Payload.SignedTxBlob;
        PaymentRequirement accepted = envelope.Accepted;

        string decodedJson = XrplBinaryCodec.Decode(signedBlob).ToString();
        using JsonDocument doc = JsonDocument.Parse(decodedJson);
        JsonElement root = doc.RootElement;

        string payer = GetString(root, "Account") ?? string.Empty;

        string? rejection = CheckTransaction(root, accepted, out uint lastLedgerSequence);
        if (rejection != null)
            return Refused(rejection);

        try
        {
            // A blob whose window has passed can no longer enter a new ledger, so a success the node
            // reports for it is an earlier settlement being replayed - possibly one the store has
            // already forgotten.
            uint validatedLedgerIndex = await _validatedLedgerIndex(cancellationToken);
            if (lastLedgerSequence <= validatedLedgerIndex)
                return Refused("payment_expired");

            string hash = HashLedger.HashSignedTx(signedBlob);
            if (!await _settled.TryReserveAsync(hash, lastLedgerSequence, validatedLedgerIndex, cancellationToken))
                return Refused("duplicate_payment");

            bool keepClaim = false;
            try
            {
                PaymentResponseEnvelope response = await SettleAsync(signedBlob, accepted, payer, cancellationToken);

                // Funds moved once a payment validated, even if it delivered too little: the claim
                // stays so the same blob cannot be presented again. Anything else may be retried.
                keepClaim = response.Success || response.ErrorReason == "delivered_amount_mismatch";
                return response;
            }
            finally
            {
                if (!keepClaim)
                    await _settled.ReleaseAsync(hash, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Refused("settlement_error");
        }
    }

    private async Task<PaymentResponseEnvelope> SettleAsync(
        string signedBlob,
        PaymentRequirement accepted,
        string payer,
        CancellationToken cancellationToken)
    {
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
                        Network = accepted.Network,
                        Payer = payer
                    };
                }
            }

            string? txResult = summary.Meta?.TransactionResult;
            bool succeeded = summary.Validated
                && txResult is string r
                && r.StartsWith("tes", StringComparison.Ordinal);

            if (!succeeded)
                return Refused("settlement_failed");

            // delivered_amount, not Amount: only the metadata says what the destination received.
            if (!Covers(summary.Meta!.ActuallyDeliveredAmount, accepted))
            {
                return new PaymentResponseEnvelope
                {
                    Success = false,
                    ErrorReason = "delivered_amount_mismatch",
                    Transaction = summary.Hash,
                    Network = accepted.Network,
                    Payer = payer
                };
            }

            return new PaymentResponseEnvelope
            {
                Success = true,
                Transaction = summary.Hash,
                Network = accepted.Network,
                Payer = payer
            };
        }
        catch (Exception ex) when (ex is TransactionFailedException or TransactionExpiredException)
        {
            return Refused("settlement_failed");
        }
    }

    /// <summary>
    /// Whether the signed transaction pays what the requirement asks, before anything is submitted.
    /// Returns the error reason, or <c>null</c> when it does.
    /// </summary>
    private static string? CheckTransaction(JsonElement tx, PaymentRequirement accepted, out uint lastLedgerSequence)
    {
        lastLedgerSequence = 0;

        // Other transaction types carry a Destination too (CheckCreate, EscrowCreate) and end in
        // tesSUCCESS without paying anything.
        if (!string.Equals(GetString(tx, "TransactionType"), "Payment", StringComparison.Ordinal))
            return "invalid_transaction_type";

        if (!string.Equals(GetString(tx, "Destination"), accepted.PayTo, StringComparison.Ordinal))
            return "invalid_destination";

        // A partial payment may deliver any fraction of Amount and still succeed.
        uint flags = tx.TryGetProperty("Flags", out JsonElement flagsEl) && flagsEl.ValueKind == JsonValueKind.Number
            ? flagsEl.GetUInt32()
            : 0;
        if ((flags & PartialPaymentFlag) != 0)
            return "invalid_partial_payment";

        if (!tx.TryGetProperty("Amount", out JsonElement amount))
            return "invalid_amount";

        string? amountRejection = CheckAmount(amount, accepted);
        if (amountRejection != null)
            return amountRejection;

        // Without a LastLedgerSequence the blob stays valid forever, and no store could hold its
        // hash for that long.
        if (!tx.TryGetProperty("LastLedgerSequence", out JsonElement llsEl)
            || llsEl.ValueKind != JsonValueKind.Number
            || !llsEl.TryGetUInt32(out lastLedgerSequence))
        {
            return "invalid_last_ledger_sequence";
        }

        return null;
    }

    private static string? CheckAmount(JsonElement amount, PaymentRequirement accepted)
    {
        if (X402AmountMapper.IsXrp(accepted))
        {
            if (amount.ValueKind != JsonValueKind.String)
                return "invalid_asset";

            return TryParseDrops(amount.GetString(), out ulong paid)
                && TryParseDrops(accepted.Amount, out ulong required)
                && paid == required
                    ? null
                    : "invalid_amount";
        }

        // The requirement model names XRP or an issued currency; an MPT amount matches neither.
        if (amount.ValueKind != JsonValueKind.Object || amount.TryGetProperty("mpt_issuance_id", out _))
            return "invalid_asset";

        string? requiredIssuer = RequiredIssuer(accepted);
        if (requiredIssuer == null
            || !SameCurrency(GetString(amount, "currency"), accepted.Asset)
            || !string.Equals(GetString(amount, "issuer"), requiredIssuer, StringComparison.Ordinal))
        {
            return "invalid_asset";
        }

        return TryParseTokenValue(GetString(amount, "value"), out decimal paidValue)
            && TryParseTokenValue(accepted.Amount, out decimal requiredValue)
            && paidValue == requiredValue
                ? null
                : "invalid_amount";
    }

    /// <summary>Whether the delivered amount is the required asset, at least the required amount.</summary>
    private static bool Covers(Currency? delivered, PaymentRequirement accepted)
    {
        if (delivered == null || !string.IsNullOrEmpty(delivered.MPTokenIssuanceID))
            return false;

        if (X402AmountMapper.IsXrp(accepted))
        {
            return string.Equals(delivered.CurrencyCode, "XRP", StringComparison.Ordinal)
                && TryParseDrops(delivered.Value, out ulong deliveredDrops)
                && TryParseDrops(accepted.Amount, out ulong requiredDrops)
                && deliveredDrops >= requiredDrops;
        }

        string? requiredIssuer = RequiredIssuer(accepted);
        return requiredIssuer != null
            && !string.Equals(delivered.CurrencyCode, "XRP", StringComparison.Ordinal)
            && SameCurrency(delivered.CurrencyCode, accepted.Asset)
            && string.Equals(delivered.Issuer, requiredIssuer, StringComparison.Ordinal)
            && TryParseTokenValue(delivered.Value, out decimal deliveredValue)
            && TryParseTokenValue(accepted.Amount, out decimal requiredValue)
            && deliveredValue >= requiredValue;
    }

    private static string? RequiredIssuer(PaymentRequirement accepted) =>
        accepted.Extra.TryGetValue("issuer", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool TryParseDrops(string? value, out ulong drops) =>
        ulong.TryParse(value, DropsStyle, CultureInfo.InvariantCulture, out drops);

    // decimal covers every amount an x402 price is set in; a value beyond it fails closed.
    private static bool TryParseTokenValue(string? value, out decimal parsed) =>
        decimal.TryParse(value, TokenValueStyle, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    /// <summary>
    /// Compares currency codes in their 160-bit form, since the requirement may name a code as
    /// <c>USD</c>, <c>RLUSD</c> or 40 hex digits, and the decoder renders a standard code as three
    /// letters and any other one as hex.
    /// </summary>
    private static bool SameCurrency(string? actual, string required)
    {
        string? actualHex = CurrencyHex(actual);
        return actualHex != null && string.Equals(actualHex, CurrencyHex(required), StringComparison.Ordinal);
    }

    private static string? CurrencyHex(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, "XRP", StringComparison.Ordinal))
            return null;

        if (code.Length == 40 && IsHex(code))
            return code.ToUpperInvariant();

        if (code.Length == 3)
            return new string('0', 24) + Convert.ToHexString(Encoding.ASCII.GetBytes(code)) + new string('0', 10);

        // A non-standard code written as text, as t54 names RLUSD: its ASCII bytes, zero-padded.
        if (code.Length <= 20)
            return Convert.ToHexString(Encoding.ASCII.GetBytes(code)).PadRight(40, '0');

        return null;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return true;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static PaymentResponseEnvelope Refused(string reason) =>
        new() { Success = false, ErrorReason = reason };
}
