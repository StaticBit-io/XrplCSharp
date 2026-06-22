using System.Collections.Generic;
using System.Text.Json;
using Xrpl.Models.Transactions;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

public static class X402PaymentBuilder
{
    public const uint DefaultSourceTag = 804681468;

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
