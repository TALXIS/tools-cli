using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>How a connection's owner is shown wherever a user is asked about it.</summary>
internal static class ConnectionDisplay
{
    public static string? Owner(ConnectionDetail connection)
        => connection.OwnerName is { } name
            ? connection.OwnerPrincipalName is { } upn ? $"{name} <{upn}>" : name
            : null;

    /// <summary>
    /// The connection behind a bound reference, for showing its owner and
    /// status next to it; null when it cannot be read. The reference is still
    /// listed then, just without those two columns.
    /// </summary>
    public static async Task<ConnectionDetail?> TryGetAsync(
        IConnectorMetadataService metadata, string? profile, string connector, string connectionId, ILogger logger)
    {
        try
        {
            return await metadata.GetConnectionAsync(profile, connector, connectionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // A connection the caller cannot see still lists as bound.
            logger.LogDebug(ex, "Could not read connection {Connection}.", connectionId);
            return null;
        }
        catch (NotSupportedException ex)
        {
            // Connection owners are only read in the public cloud so far.
            logger.LogDebug(ex, "Connection {Connection} is not readable in this cloud.", connectionId);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // Anything but "not visible to you" (401, 5xx) leaves the row
            // without owner and status, so say why instead of only showing "-".
            logger.LogWarning("Could not read connection {Connection} ({Connector}): {Reason}", connectionId, connector, ex.Message);
            return null;
        }
    }
}
