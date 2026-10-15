using TALXIS.CLI.Core.Identity;

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
        using var payload = JwtPayload.TryParse(accessToken);
        return payload is null ? null : JsonElementReader.TryGetString(payload.RootElement, claim);
    }
}
