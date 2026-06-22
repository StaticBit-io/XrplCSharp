using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Xrpl.Models.Transactions;

namespace Xrpl.X402;

/// <summary>
/// Builds the XRPL transaction memo that identifies an x402 payment by invoice ID and optional session ID.
/// </summary>
public static class X402MemoFactory
{
    /// <summary>
    /// Creates a <see cref="MemoWrapper"/> containing a JSON payload with the given identifiers,
    /// encoded as hex strings per the XRPL memo format.
    /// </summary>
    /// <param name="paymentId">Unique identifier of the payment (e.g. the invoice ID from <c>extra.invoiceId</c>).</param>
    /// <param name="sessionId">Optional session identifier; omitted from the memo when <c>null</c>.</param>
    /// <param name="paymentIdField">JSON field name for the payment ID (default: <c>"paymentId"</c>).</param>
    /// <param name="sessionIdField">JSON field name for the session ID (default: <c>"sessionId"</c>).</param>
    /// <returns>A <see cref="MemoWrapper"/> ready to attach to an XRPL <c>Payment</c> transaction.</returns>
    public static MemoWrapper Build(string paymentId, string? sessionId,
        string paymentIdField = "paymentId", string sessionIdField = "sessionId")
    {
        Dictionary<string, object> payload = new() { [paymentIdField] = paymentId };
        if (sessionId is not null)
            payload[sessionIdField] = sessionId;
        string json = JsonSerializer.Serialize(payload);

        return new MemoWrapper
        {
            Memo = new Memo
            {
                MemoType = Hex("x402"),
                MemoFormat = Hex("application/json"),
                MemoData = Hex(json),
            }
        };
    }

    private static string Hex(string s) => Convert.ToHexString(Encoding.UTF8.GetBytes(s));
}
