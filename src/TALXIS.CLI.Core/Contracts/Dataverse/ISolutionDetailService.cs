namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Detailed solution record returned by <see cref="ISolutionDetailService.ShowAsync"/>.
/// </summary>
public sealed record SolutionDetail(
    Guid Id,
    string UniqueName,
    string? FriendlyName,
    string? Version,
    bool Managed,
    DateTime? InstalledOn,
    string? Description,
    string? PublisherName,
    string? PublisherPrefix);

/// <summary>
/// Per-component-type count row returned alongside <see cref="SolutionDetail"/>.
/// </summary>
public sealed record ComponentCountRow(
    string TypeName,
    int TypeCode,
    string? LogicalName,
    int Count);

/// <summary>
/// Retrieves detailed information about a single installed solution,
/// including a breakdown of component counts per type.
/// </summary>
public interface ISolutionDetailService
{
    /// <summary>
    /// Gets solution details and component-type counts for the solution
    /// identified by <paramref name="solutionUniqueName"/>.
    /// </summary>
    Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> ShowAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct);
}
