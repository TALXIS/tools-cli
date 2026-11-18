using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// Finds the Power Platform environment id behind a connection: the id stored on
/// the connection when there is one, otherwise the platform catalog entry for
/// the connection's Dataverse URL.
/// </summary>
public interface IEnvironmentIdResolver
{
    /// <exception cref="InvalidOperationException">The connection carries no id and its URL matches no environment.</exception>
    Task<Guid> ResolveAsync(Connection connection, Credential credential, CancellationToken ct);
}
