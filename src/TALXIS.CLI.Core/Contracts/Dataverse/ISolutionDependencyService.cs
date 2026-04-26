namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Single dependency row returned by dependency queries. Contains raw GUIDs
/// and type codes — display-name enrichment is handled downstream.
/// </summary>
public sealed record DependencyRow(
    Guid DependencyId,
    Guid DependentComponentId,
    int DependentComponentType,
    Guid DependentBaseSolutionId,
    Guid RequiredComponentId,
    int RequiredComponentType,
    Guid RequiredBaseSolutionId,
    int DependencyType);

/// <summary>
/// Dependency analysis operations for solution components.
/// </summary>
public interface ISolutionDependencyService
{
    /// <summary>
    /// Returns all dependencies that would block uninstalling the solution.
    /// An empty list means the solution can be safely uninstalled.
    /// </summary>
    Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct);

    /// <summary>
    /// Returns components that depend on the specified component
    /// ("what would break if I modify/remove this?").
    /// </summary>
    Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct);

    /// <summary>
    /// Returns components that the specified component requires
    /// ("what must exist for this to work?").
    /// </summary>
    Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct);

    /// <summary>
    /// Returns dependencies that would block deletion of the specified component.
    /// An empty list means the component can be safely deleted.
    /// </summary>
    Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        CancellationToken ct);
}
