using System.Text.Json;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Reads non-secret claims from an access token that was just acquired for the
/// current identity. The token is never validated or logged here.
/// </summary>
internal static class JwtClaims
{
    public static string? GetObjectId(string accessToken) => GetString(accessToken, "oid");

    public static string? GetString(string accessToken, string claim)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
