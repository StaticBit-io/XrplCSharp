using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Globalization;

using Xrpl.BinaryCodec;

using Xrpl.Models.Transactions;

namespace Xrpl.Sugar;

public static class LedgerSequenceHelper
{
    public static uint? GetLastLedgerSequence(object transaction)
    {
        // 1) Dictionary<string, dynamic>
        if (transaction is IDictionary<string, object> dictObj)
            return TryGetUint(dictObj, "LastLedgerSequence");

        if (transaction is Dictionary<string, dynamic> dictDyn)
            return TryGetUint(dictDyn, "LastLedgerSequence");

        // 2) твой тип
        if (transaction is TransactionRequest txc)
            return txc.LastLedgerSequence;

        // 3) JObject
        if (transaction is JObject j)
            return TryGetUint(j, "LastLedgerSequence");

        // 4) fallback: encode -> parse
        var encoded = XrplBinaryCodec.Encode(transaction);
        var json = JObject.Parse($"{encoded}");
        return TryGetUint(json, "LastLedgerSequence");
    }

    private static uint? TryGetUint(JObject j, string name)
        => j.TryGetValue(name, out var token) ? ToUInt32Nullable(token) : null;

    private static uint? TryGetUint<T>(IDictionary<string, T> dict, string key)
        => dict.TryGetValue(key, out var value) ? ToUInt32Nullable(value) : null;

    private static uint? ToUInt32Nullable(object? value)
    {
        if (value is null) return null;

        // если dynamic внутри оказался JToken/JValue
        if (value is JToken jt)
        {
            if (jt.Type == JTokenType.Null || jt.Type == JTokenType.Undefined) return null;
            value = (jt as JValue)?.Value ?? jt.ToString();
        }

        try
        {
            switch (value)
            {
                case uint u: return u;
                case int i when i >= 0: return (uint)i;
                case long l when l >= 0 && l <= uint.MaxValue: return (uint)l;
                case ulong ul when ul <= uint.MaxValue: return (uint)ul;
                case short s when s >= 0: return (uint)s;
                case ushort us: return us;
                case byte b: return b;
                case sbyte sb when sb >= 0: return (uint)sb;

                case string str:
                    str = str.Trim();
                    // на всякий: "123", "123.0"
                    if (uint.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedU))
                        return parsedU;
                    if (decimal.TryParse(str, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec) &&
                        dec >= 0 && dec <= uint.MaxValue && decimal.Truncate(dec) == dec)
                        return (uint)dec;
                    return null;

                case decimal d when d >= 0 && d <= uint.MaxValue && decimal.Truncate(d) == d:
                    return (uint)d;

                case double db when db >= 0 && db <= uint.MaxValue && Math.Floor(db) == db:
                    return (uint)db;

                case float f when f >= 0 && f <= uint.MaxValue && Math.Floor(f) == f:
                    return (uint)f;

                default:
                    // последний шанс: Convert (поймает boxed-числа)
                    var conv = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                    return conv <= uint.MaxValue ? (uint)conv : null;
            }
        }
        catch
        {
            return null;
        }
    }
}
