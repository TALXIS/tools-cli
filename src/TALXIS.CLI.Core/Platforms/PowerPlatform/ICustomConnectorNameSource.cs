namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>A custom connector's stable logical name and the ids it is known by in one environment.</summary>
/// <param name="ConnectorRowId">The Dataverse <c>connector</c> row id, when known.</param>
/// <param name="ApiId">The environment's apiId (<c>connectorinternalid</c>), when known.</param>
public sealed record CustomConnectorName(string LogicalName, Guid? ConnectorRowId, string? ApiId);

/// <summary>
/// The logical names of the environment's custom connectors. They live in
/// Dataverse, so the control plane reads them through this contract instead of
/// querying Dataverse itself.
/// </summary>
public interface ICustomConnectorNameSource
{
    /// <summary>The apiId of the custom connector with this logical name, or null when there is none or Dataverse cannot say.</summary>
    Task<string?> TryFindApiIdAsync(string? profileName, string logicalName, CancellationToken ct);

    /// <summary>Every custom connector's name; empty when Dataverse cannot say.</summary>
    Task<IReadOnlyList<CustomConnectorName>> ReadAllAsync(string? profileName, CancellationToken ct);
}
