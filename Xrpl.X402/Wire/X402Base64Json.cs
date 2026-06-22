using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

public static class X402Base64Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Encode<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static T Decode<T>(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64.Trim());
        string json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new FormatException($"X402: cannot decode {typeof(T).Name}");
    }
}
