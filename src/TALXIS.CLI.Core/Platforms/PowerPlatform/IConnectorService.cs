namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>Whether a connector ships with the platform or was built for the environment.</summary>
public static class ConnectorKind
{
    public const string BuiltIn = "built-in";
    public const string Custom = "custom";
}

/// <summary>
/// A connector in the environment's catalog.
/// </summary>
/// <param name="LogicalName">
/// The name to use for <c>--connector</c>: the stable logical name of a custom
/// connector, or the apiId of a built-in one.
/// </param>
/// <param name="ApiId">The environment's connector id; a custom connector's carries an environment-specific hash.</param>
/// <param name="Kind"><see cref="ConnectorKind"/>.</param>
public sealed record ConnectorListItem(
    string LogicalName,
    string ApiId,
    string? DisplayName,
    string Kind,
    string? Tier,
    string? Description);

/// <summary>Browses the connectors available in the environment bound to the active profile.</summary>
public interface IConnectorService
{
    /// <param name="filter">Substring of the logical name, apiId or display name.</param>
    /// <param name="kind"><see cref="ConnectorKind"/> value, or null for both.</param>
    Task<IReadOnlyList<ConnectorListItem>> ListAsync(
        string? profileName, string? filter, string? kind, int? top, CancellationToken ct);
}
