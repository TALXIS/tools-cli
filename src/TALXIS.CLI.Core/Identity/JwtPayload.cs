using System.Buffers.Text;
using System.Text.Json;

namespace TALXIS.CLI.Core.Identity;

/// <summary>Reads the payload of an access token. The token is never validated.</summary>
public static class JwtPayload
{
    /// <summary>The payload as JSON, or null when <paramref name="accessToken"/> is not a JWT.</summary>
    public static JsonDocument? TryParse(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
