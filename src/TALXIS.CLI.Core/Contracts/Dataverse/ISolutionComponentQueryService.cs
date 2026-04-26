namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Row from <c>msdyn_solutioncomponentsummaries</c> virtual entity.
/// </summary>
public sealed record ComponentSummaryRow(
    string TypeName,
    int TypeCode,
    string? DisplayName,
    string? Name,
    string ObjectId,
    bool Managed,
    bool Customizable);

/// <summary>
/// Read-only queries against solution components: listing and counting.
/// </summary>
public interface ISolutionComponentQueryService
{
    /// <summary>
    /// Lists components in the given solution, optionally filtered by type.
    /// </summary>
    Task<IReadOnlyList<ComponentSummaryRow>> ListAsync(
        string? profileName,
        string solutionUniqueName,
        int? componentTypeFilter,
        string? entityFilter,
        int? top,
        CancellationToken ct);

    /// <summary>
    /// Returns per-component-type counts for a solution.
    /// </summary>
    Task<IReadOnlyList<ComponentCountRow>> CountAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct);
}
