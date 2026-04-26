namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Single layer row from the <c>msdyn_componentlayers</c> virtual entity.
/// Each row represents one solution's contribution to a component's definition.
/// </summary>
public sealed record ComponentLayerRow(
    int Order,
    string SolutionName,
    string? PublisherName,
    string? Name,
    DateTime OverwriteTime,
    string? ComponentJson,
    string? Changes);

/// <summary>
/// Read-only queries against solution layers for individual components.
/// </summary>
public interface ISolutionLayerQueryService
{
    /// <summary>
    /// Returns the full solution layer stack for a component,
    /// ordered from bottom (base) to top (active).
    /// </summary>
    Task<IReadOnlyList<ComponentLayerRow>> ListLayersAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct);

    /// <summary>
    /// Returns the active layer's component definition JSON, or null
    /// if no active layer exists.
    /// </summary>
    Task<string?> GetActiveLayerJsonAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct);
}
