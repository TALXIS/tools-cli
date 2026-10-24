using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Resolves the current environment type (Production, Sandbox, etc.) for a
/// connection by querying the provider's control plane API. Implemented by
/// each platform adapter (Dataverse → Power Platform admin API, etc.).
/// </summary>
public interface IEnvironmentTypeResolver
{
    /// <summary>
    /// Queries the control plane for the environment type and updates the
    /// connection's <see cref="Connection.EnvironmentType"/> (and related
    /// metadata like <see cref="Connection.DisplayName"/>) in-place.
    /// Persists the updated connection to the store.
    /// Returns the resolved environment type, or null if the lookup failed.
    /// </summary>
    Task<EnvironmentType?> RefreshAsync(Connection connection, Credential credential, CancellationToken ct);
}

public static class EnvironmentTypeResolverExtensions
{
    /// <summary>
    /// Refreshes <paramref name="connection"/> and returns its resulting type.
    /// Never throws: on any failure (control-plane error, timeout, failed save)
    /// it warns and returns the type the connection already had.
    /// </summary>
    public static async Task<EnvironmentType?> TryRefreshAsync(
        this IEnvironmentTypeResolver resolver,
        Connection connection,
        Credential credential,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            return await resolver.RefreshAsync(connection, credential, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Could not refresh the environment type of connection '{ConnectionId}' ({Error}); keeping the recorded value ({EnvType}).",
                connection.Id, ex.Message, connection.EnvironmentType?.ToString() ?? "unknown");
            return connection.EnvironmentType;
        }
    }
}
