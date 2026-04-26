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
/// Extended incrementally — starts with <see cref="CheckUninstallAsync"/>;
/// additional methods added when the corresponding CLI commands land.
/// </summary>
public interface ISolutionDependencyService
{
    /// <summary>
    /// Returns all dependencies that would block uninstalling the solution
    /// identified by <paramref name="solutionUniqueName"/>.
    /// An empty list means the solution can be safely uninstalled.
    /// </summary>
    Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct);
}
