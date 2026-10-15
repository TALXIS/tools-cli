namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>A connector as the active environment knows it.</summary>
/// <param name="ApiId">Short connector id, for example <c>shared_office365users</c>. For a custom connector it carries an environment-specific hash.</param>
/// <param name="LogicalName">
/// Stable, solution-aware logical name of a custom connector (identical in every
/// environment); for a built-in connector the logical name is the apiId.
/// </param>
/// <param name="ConnectorRowId">Dataverse <c>connector</c> row id of a custom connector, when known.</param>
public sealed record ResolvedConnectorInfo(
    string ApiId,
    string LogicalName,
    string? DisplayName,
    bool IsCustom,
    Guid? ConnectorRowId);

/// <summary>
/// Turns what a person types for a connector (logical name, apiId in either
/// form, or an exact display name) into the environment's connector. Lets
/// features that live in other layers share one resolution rule.
/// </summary>
public interface IConnectorResolver
{
    /// <exception cref="ArgumentException">The connector is unknown or the name is ambiguous.</exception>
    Task<ResolvedConnectorInfo> ResolveAsync(string? profileName, string connector, CancellationToken ct);
}
