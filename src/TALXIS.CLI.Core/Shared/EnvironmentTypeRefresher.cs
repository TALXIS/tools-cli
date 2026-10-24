using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core;

/// <summary>
/// Best-effort refresh of a connection's environment type through the
/// registered <see cref="IEnvironmentTypeResolver"/>. Shared by the
/// destructive-operation guard and <c>config profile validate --refresh-env-type</c>
/// so both treat control-plane failures the same way: warn and keep whatever
/// type is already recorded.
/// </summary>
public static class EnvironmentTypeRefresher
{
    /// <summary>
    /// Refreshes <paramref name="connection"/> in place and returns its resulting
    /// type. Never throws; on any failure (resolver not registered, control-plane
    /// error, timeout) returns the connection's current type unchanged.
    /// </summary>
    public static async Task<EnvironmentType?> TryRefreshAsync(
        Connection connection,
        Credential credential,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var resolver = TxcServices.Get<IEnvironmentTypeResolver>();
            return await resolver.RefreshAsync(connection, credential, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not refresh the environment type of connection '{ConnectionId}'; keeping the recorded value ({EnvType}).",
                connection.Id, connection.EnvironmentType?.ToString() ?? "unknown");
            return connection.EnvironmentType;
        }
    }
}
