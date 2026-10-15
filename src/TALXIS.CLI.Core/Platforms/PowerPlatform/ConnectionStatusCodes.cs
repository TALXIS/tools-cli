namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>Status and error codes the platform reports for connections, matched verbatim.</summary>
public static class ConnectionStatusCodes
{
    /// <summary>Status of a healthy connection.</summary>
    public const string Connected = "Connected";

    /// <summary>Error code of a connection whose consent or sign-in is still pending.</summary>
    public const string Unauthenticated = "Unauthenticated";

    /// <summary>Error marker of a request that named a connection that does not exist.</summary>
    public const string ConnectionNotFound = "ConnectionNotFound";

    /// <summary>Error marker of a request that named a connection the caller may not use.</summary>
    public const string ConnectionAuthorizationFailed = "ConnectionAuthorizationFailed";

    /// <summary>True for <see cref="Connected"/>, whatever the casing.</summary>
    public static bool IsConnected(string? status)
        => string.Equals(status, Connected, StringComparison.OrdinalIgnoreCase);
}
