using System.Collections.Generic;
using System.Text.Json;
using Xrpl.Models.Transactions;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

public static class X402PaymentBuilder
{
    public const uint DefaultSourceTag = 804681468;

    public static Payment Build(PaymentRequirement req, string payerAddress)
    {
        string invoiceId = GetString(req.Extra, "invoiceId")
            ?? throw new X402PaymentException("invalid_requirement", "missing extra.invoiceId");
        string? sessionId = GetString(req.Extra, "sessionId");
        uint sourceTag = GetUInt(req.Extra, "sourceTag") ?? DefaultSourceTag;

        return new Payment
        {
            Account = payerAddress,
            Destination = req.PayTo,
            Amount = X402AmountMapper.ToCurrency(req),
            SourceTag = sourceTag,
            Memos = new List<MemoWrapper> { X402MemoFactory.Build(invoiceId, sessionId) },
        };
    }

    private static string? GetString(Dictionary<string, JsonElement> extra, string key) =>
        extra.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static uint? GetUInt(Dictionary<string, JsonElement> extra, string key) =>
        extra.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.Number ? el.GetUInt32() : null;
}
