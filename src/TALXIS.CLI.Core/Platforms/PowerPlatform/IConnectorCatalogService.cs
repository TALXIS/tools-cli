namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// Read-only discovery over the connectors available in the target
/// environment and their operations, backed by the Power Apps connector API.
/// Exists so agents authoring cloud flow definitions can look up exact
/// operation ids and parameter names instead of guessing them.
/// </summary>
public interface IConnectorCatalogService
{
    /// <summary>Lists connectors available in the profile's environment.</summary>
    Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName,
        CancellationToken ct);

    /// <summary>Lists all operations (actions and triggers) of one connector.</summary>
    Task<IReadOnlyList<ConnectorOperationSummary>> ListOperationsAsync(
        string? profileName,
        string connectorName,
        CancellationToken ct);

    /// <summary>
    /// Returns parameter-level detail for one operation, or null when the
    /// operation does not exist on the connector.
    /// </summary>
    Task<ConnectorOperationDetail?> GetOperationAsync(
        string? profileName,
        string connectorName,
        string operationId,
        CancellationToken ct);
}
