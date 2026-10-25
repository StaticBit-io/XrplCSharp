using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.AddressCodec;
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
/// (with the destination tag an X-address or <c>extra.destinationTag</c> names) of exactly
/// <see cref="PaymentRequirement.Amount"/> in <see cref="PaymentRequirement.Asset"/> (and
/// <c>extra.issuer</c> for an issued currency), without <c>tfPartialPayment</c>, bound to
/// <c>extra.invoiceId</c> when there is one, with a <c>LastLedgerSequence</c> that has not passed
/// and does not reach further than the requirement's timeout allows. After validation, the
/// metadata's <c>delivered_amount</c> must cover the requirement. Each refusal has its own
/// <see cref="PaymentResponseEnvelope.ErrorReason"/>: <c>invalid_transaction_type</c>,
/// <c>invalid_destination</c>, <c>invalid_partial_payment</c>, <c>invalid_asset</c>,
/// <c>invalid_amount</c>, <c>invalid_invoice_id</c>, <c>invalid_last_ledger_sequence</c>,
/// <c>payment_expired</c>, <c>duplicate_payment</c> and <c>delivered_amount_mismatch</c>.
/// </para>
/// <para>
/// Everything is checked against <see cref="PaymentSignatureEnvelope.Accepted"/>, which must be the
/// server's requirement; <c>RequirePayment</c> puts it there.
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

    // Faster than any network closes ledgers, so a payer that assumed a slower close still fits.
    private const double MinSecondsPerLedger = 3.0;

    private const uint AutofillWindowLedgers = 20;

    // The payer's node may be ahead of the facilitator's; its validated ledger is where autofill counts from.
    private const uint NodeSkewLedgers = 10;

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

            if ((ulong)lastLedgerSequence > (ulong)validatedLedgerIndex + MaxWindowLedgers(accepted))
                return Refused("invalid_last_ledger_sequence");

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
                    await TryReleaseAsync(hash);
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

        if (!DestinationMatches(tx, accepted))
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

        // Without this a blob paid under one invoice settles another of the same price and payee.
        if (!InvoiceBound(tx, accepted))
            return "invalid_invoice_id";

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

    /// <summary>
    /// Whether the transaction pays <see cref="PaymentRequirement.PayTo"/>. An X-address there carries
    /// a destination tag, as does <c>extra.destinationTag</c>; either must then be the transaction's
    /// <c>DestinationTag</c>, since an exchange or custodian credits by the tag.
    /// </summary>
    private static bool DestinationMatches(JsonElement tx, PaymentRequirement accepted)
    {
        string payTo = accepted.PayTo;
        uint? requiredTag = null;

        // The binary codec stores a classic address and a separate tag, so an X-address never
        // appears in a decoded blob.
        if (!payTo.StartsWith('r') && XrplAddressCodec.IsValidXAddress(payTo))
        {
            XrplAddressCodec.CodecAddress decoded = XrplAddressCodec.XAddressToClassicAddress(payTo);
            payTo = decoded.ClassicAddress;
            requiredTag = decoded.Tag;
        }

        if (accepted.Extra.TryGetValue("destinationTag", out JsonElement tagEl))
        {
            if (tagEl.ValueKind != JsonValueKind.Number
                || !tagEl.TryGetUInt32(out uint extraTag)
                || (requiredTag.HasValue && requiredTag.Value != extraTag))
            {
                return false;
            }

            requiredTag = extraTag;
        }

        if (!string.Equals(GetString(tx, "Destination"), payTo, StringComparison.Ordinal))
            return false;

        return requiredTag is not uint tag
            || (tx.TryGetProperty("DestinationTag", out JsonElement txTag)
                && txTag.ValueKind == JsonValueKind.Number
                && txTag.TryGetUInt32(out uint paidTag)
                && paidTag == tag);
    }

    /// <summary>
    /// Whether a requirement's <c>extra.invoiceId</c> is bound into the transaction the way the t54
    /// reference payer binds it: <c>InvoiceID</c> = SHA-256 of its UTF-8 bytes, or a memo whose
    /// <c>MemoData</c> is those bytes in hex. Either is enough, since the client chooses the binding
    /// (<c>X402IntentBinding</c>). A requirement without an invoice id binds nothing.
    /// </summary>
    private static bool InvoiceBound(JsonElement tx, PaymentRequirement accepted)
    {
        if (!accepted.Extra.TryGetValue("invoiceId", out JsonElement invoiceEl))
            return true;

        string? invoiceId = invoiceEl.ValueKind == JsonValueKind.String ? invoiceEl.GetString() : null;
        if (string.IsNullOrEmpty(invoiceId))
            return false;

        byte[] invoiceBytes = Encoding.UTF8.GetBytes(invoiceId);
        if (string.Equals(GetString(tx, "InvoiceID"), Convert.ToHexString(SHA256.HashData(invoiceBytes)), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!tx.TryGetProperty("Memos", out JsonElement memos) || memos.ValueKind != JsonValueKind.Array)
            return false;

        string memoData = Convert.ToHexString(invoiceBytes);
        foreach (JsonElement wrapper in memos.EnumerateArray())
        {
            if (wrapper.ValueKind == JsonValueKind.Object
                && wrapper.TryGetProperty("Memo", out JsonElement memo)
                && memo.ValueKind == JsonValueKind.Object
                && string.Equals(GetString(memo, "MemoData"), memoData, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The furthest <c>LastLedgerSequence</c> accepted past the validated ledger. It bounds how long a
    /// settled hash has to be kept, and is wide enough for any honest payer: <c>XrplWalletX402Signer</c>
    /// sets the open ledger plus <c>MaxTimeoutSeconds</c> at 4 seconds a ledger, and autofill without a
    /// timeout sets the validated ledger plus 20.
    /// </summary>
    private static uint MaxWindowLedgers(PaymentRequirement accepted) =>
        accepted.MaxTimeoutSeconds > 0
            ? (uint)Math.Max(Math.Ceiling(accepted.MaxTimeoutSeconds / MinSecondsPerLedger), AutofillWindowLedgers) + 1 + NodeSkewLedgers
            : AutofillWindowLedgers + 1 + NodeSkewLedgers;

    // A store that cannot drop the claim must not cost the payer the response already built: a
    // settlement_unknown carries the hash they reconcile against. The claim then lapses with the window.
    private async ValueTask TryReleaseAsync(string hash)
    {
        try
        {
            await _settled.ReleaseAsync(hash, CancellationToken.None);
        }
        catch
        {
        }
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
