using System;
using System.Text;
using System.Text.Json;
using Xrpl.Models.Transactions;

namespace Xrpl.X402;

public static class X402MemoFactory
{
    public static MemoWrapper Build(string paymentId, string? sessionId)
    {
        object payload = sessionId is null
            ? new { paymentId }
            : new { paymentId, sessionId };
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
