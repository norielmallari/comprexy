using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Comprexy.Application.Services;

/// <summary>
/// Correlation hashes for metrics without storing prompt bodies.
/// </summary>
public static class MetricsPayloadHasher
{
    public static string HashJsonElement(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        return HashUtf8(element.Value.GetRawText());
    }

    public static string HashUtf8(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
