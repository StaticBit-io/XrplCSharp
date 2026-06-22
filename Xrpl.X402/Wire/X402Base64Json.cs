using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

/// <summary>
/// Serializes and deserializes x402 wire objects as Base64-encoded UTF-8 JSON,
/// matching the encoding used in the <c>PAYMENT-REQUIRED</c>, <c>PAYMENT-SIGNATURE</c>,
/// and <c>PAYMENT-RESPONSE</c> HTTP headers.
/// </summary>
public static class X402Base64Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON and returns a Base64-encoded UTF-8 string.
    /// </summary>
    /// <typeparam name="T">Type of the value to serialize.</typeparam>
    /// <param name="value">The object to encode.</param>
    /// <returns>Base64 string containing the UTF-8 JSON representation of <paramref name="value"/>.</returns>
    public static string Encode<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Decodes a Base64-encoded UTF-8 JSON string and deserializes it to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type to deserialize into.</typeparam>
    /// <param name="base64">Base64 string to decode and deserialize.</param>
    /// <returns>The deserialized object.</returns>
    /// <exception cref="FormatException">Thrown when the JSON cannot be deserialized to <typeparamref name="T"/>.</exception>
    public static T Decode<T>(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64.Trim());
        string json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new FormatException($"X402: cannot decode {typeof(T).Name}");
    }
}
