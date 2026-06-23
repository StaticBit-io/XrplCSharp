using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xrpl.Models.Common;
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
    /// Builds a <see cref="Payment"/> transaction for the given requirement and payer address.
    /// <para>
    /// Reads <c>invoiceId</c> (or the configured <see cref="X402ClientOptions.InvoiceIdExtraKey"/>),
    /// <c>sourceTag</c>, and <c>destinationTag</c> from <see cref="PaymentRequirement.Extra"/>.
    /// The invoice id may be any non-empty string; it does not need to be a 64-hex value.
    /// </para>
    /// <para>
    /// Binding behaviour (t54 reference payer, default <see cref="X402IntentBinding.Both"/>):
    /// <list type="bullet">
    ///   <item><c>InvoiceID</c> field = SHA-256(UTF-8(invoiceId)), uppercase hex.</item>
    ///   <item>Memo MemoData = UTF-8 hex of the raw invoiceId string, uppercase. No MemoType / MemoFormat.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="req">The payment requirement that specifies the destination, amount, and extra data.</param>
    /// <param name="payerAddress">Classic XRPL address of the account that will sign and fund the payment.</param>
    /// <param name="options">Client options controlling binding mode; uses defaults when <c>null</c>.</param>
    /// <returns>
    /// An unsigned <see cref="Payment"/> transaction ready for autofill and signing,
    /// and the resolved raw invoice id string.
    /// </returns>
    /// <exception cref="X402PaymentException">
    /// Thrown when <c>extra.invoiceId</c> (or the configured key) is missing or empty.
    /// </exception>
    public static (Payment Payment, string InvoiceId) BuildWithInvoiceId(
        PaymentRequirement req, string payerAddress, X402ClientOptions? options = null)
    {
        options ??= new X402ClientOptions();

        string inv = GetString(req.Extra, options.InvoiceIdExtraKey)
            ?? throw new X402PaymentException("invalid_requirement", $"missing extra.{options.InvoiceIdExtraKey}");

        if (inv.Length == 0)
            throw new X402PaymentException("invalid_requirement", $"extra.{options.InvoiceIdExtraKey} must not be empty");

        uint? sourceTag = GetUInt(req.Extra, "sourceTag");
        uint? destinationTag = GetUInt(req.Extra, "destinationTag");

        Currency amount = X402AmountMapper.ToCurrency(req);

        Payment payment = new()
        {
            Account = payerAddress,
            Destination = req.PayTo,
            Amount = amount,
        };

        if (sourceTag.HasValue)
            payment.SourceTag = sourceTag.Value;

        if (destinationTag.HasValue)
            payment.DestinationTag = destinationTag.Value;

        // IOU: set SendMax to the same issued currency and value (t54 reference payer line 202-207)
        if (!X402AmountMapper.IsXrp(req))
        {
            payment.SendMax = new Currency
            {
                CurrencyCode = amount.CurrencyCode,
                Issuer = amount.Issuer,
                Value = amount.Value,
            };
        }

        // Binding: InvoiceID field = SHA-256(UTF-8(inv)), uppercase
        if (options.IntentBinding == X402IntentBinding.InvoiceIdField ||
            options.IntentBinding == X402IntentBinding.Both)
        {
            payment.InvoiceID = Sha256HexUpper(inv);
        }

        // Binding: Memo MemoData = UTF-8 hex of the raw invoiceId (only MemoData, no MemoType/MemoFormat)
        if (options.IntentBinding == X402IntentBinding.Memo ||
            options.IntentBinding == X402IntentBinding.Both)
        {
            payment.Memos = new List<MemoWrapper>
            {
                new MemoWrapper
                {
                    Memo = new Memo { MemoData = HexUpper(inv) }
                }
            };
        }

        return (payment, inv);
    }

    /// <summary>
    /// Builds a <see cref="Payment"/> transaction. Convenience overload that discards the resolved invoice id.
    /// </summary>
    public static Payment Build(PaymentRequirement req, string payerAddress, X402ClientOptions? options = null)
        => BuildWithInvoiceId(req, payerAddress, options).Payment;

    /// <summary>Encodes a string as uppercase hex of its UTF-8 bytes (t54 MemoData format).</summary>
    internal static string HexUpper(string s) => Convert.ToHexString(Encoding.UTF8.GetBytes(s));

    /// <summary>Returns SHA-256(UTF-8(s)) as uppercase hex (t54 InvoiceID field format).</summary>
    internal static string Sha256HexUpper(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string? GetString(Dictionary<string, JsonElement> extra, string key) =>
        extra.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static uint? GetUInt(Dictionary<string, JsonElement> extra, string key) =>
        extra.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.Number ? el.GetUInt32() : null;
}
