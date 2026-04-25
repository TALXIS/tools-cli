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
