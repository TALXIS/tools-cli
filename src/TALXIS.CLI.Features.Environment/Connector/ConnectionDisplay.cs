using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>How a connection's owner is shown wherever a user is asked about it.</summary>
internal static class ConnectionDisplay
{
    public static string? Owner(ConnectionDetail connection)
        => connection.OwnerName is { } name
            ? connection.OwnerPrincipalName is { } upn ? $"{name} <{upn}>" : name
            : null;
}
