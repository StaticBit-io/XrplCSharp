using System.Text.Json;
using Xrpl.Models.Common;
using Xrpl.X402.Wire;

namespace Xrpl.X402;

public static class X402AmountMapper
{
    public static bool IsXrp(PaymentRequirement req) =>
        string.Equals(req.Asset, "XRP", System.StringComparison.OrdinalIgnoreCase);

    public static Currency ToCurrency(PaymentRequirement req)
    {
        if (IsXrp(req))
            return new Currency { CurrencyCode = "XRP", Value = req.Amount };

        string issuer = req.Extra.TryGetValue("issuer", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()!
            : throw new X402PaymentException("invalid_requirement", "IOU asset requires extra.issuer");

        return new Currency { CurrencyCode = req.Asset, Issuer = issuer, Value = req.Amount };
    }
}
