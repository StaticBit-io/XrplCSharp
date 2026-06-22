using System.Collections.Generic;
using System.Text.Json;
using Xrpl.Models.Transactions;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

/// <summary>
/// Constructs an XRPL <see cref="Payment"/> transaction from an x402 <see cref="PaymentRequirement"/>,
/// ready to be autofilled and signed by an <see cref="IX402Signer"/>.
/// </summary>
public static class X402PaymentBuilder
{
    /// <summary>
    /// Default XRPL source tag applied to x402 payments when not overridden by <c>extra.sourceTag</c>.
    /// </summary>
    public const uint DefaultSourceTag = 804681468;

    /// <summary>
    /// Builds a <see cref="Payment"/> transaction for the given requirement and payer address.
    /// Reads <c>invoiceId</c>, <c>sessionId</c>, and <c>sourceTag</c> from <see cref="PaymentRequirement.Extra"/>.
    /// </summary>
    /// <param name="req">The payment requirement that specifies the destination, amount, and extra data.</param>
    /// <param name="payerAddress">Classic XRPL address of the account that will sign and fund the payment.</param>
    /// <param name="options">Client options controlling field name mappings and defaults; uses defaults when <c>null</c>.</param>
    /// <returns>An unsigned <see cref="Payment"/> transaction ready for autofill and signing.</returns>
    /// <exception cref="X402PaymentException">
    /// Thrown when <c>extra.invoiceId</c> (or the configured key) is missing from the requirement.
    /// </exception>
    public static Payment Build(PaymentRequirement req, string payerAddress, X402ClientOptions? options = null)
    {
        options ??= new X402ClientOptions();
        string invoiceId = GetString(req.Extra, options.InvoiceIdExtraKey)
            ?? throw new X402PaymentException("invalid_requirement", $"missing extra.{options.InvoiceIdExtraKey}");
        string? sessionId = GetString(req.Extra, options.SessionIdExtraKey);
        uint sourceTag = GetUInt(req.Extra, "sourceTag") ?? DefaultSourceTag;

        return new Payment
        {
            Account = payerAddress,
            Destination = req.PayTo,
            Amount = X402AmountMapper.ToCurrency(req),
            SourceTag = sourceTag,
            Memos = new List<MemoWrapper> { X402MemoFactory.Build(invoiceId, sessionId, options.MemoPaymentIdField, options.MemoSessionIdField) },
        };
    }

    private static string? GetString(Dictionary<string, JsonElement> extra, string key) =>
        extra.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static uint? GetUInt(Dictionary<string, JsonElement> extra, string key) =>
        extra.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.Number ? el.GetUInt32() : null;
}
