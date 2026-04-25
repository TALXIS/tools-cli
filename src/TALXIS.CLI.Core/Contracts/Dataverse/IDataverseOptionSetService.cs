namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Summary row for a global option set, returned by
/// <see cref="IDataverseOptionSetService.ListGlobalOptionSetsAsync"/>.
/// </summary>
public sealed record GlobalOptionSetSummaryRecord(
    string Name,
    string? DisplayName,
    string OptionSetType,
    int OptionCount,
    bool IsCustomOptionSet);

/// <summary>
/// Service for managing global and local option sets in Dataverse.
/// Covers creating global option sets, adding/removing individual options,
/// and listing all global option sets in the environment.
/// </summary>
public interface IDataverseOptionSetService
{
    /// <summary>
    /// Creates a new global option set in Dataverse.
    /// </summary>
    Task CreateGlobalOptionSetAsync(
        string? profileName,
        string schemaName,
        string displayName,
        string? description,
        OptionMetadataInput[] options,
        string? solution,
        CancellationToken ct);

    /// <summary>
    /// Inserts a new option value into a local or global option set.
    /// For a local option set, provide <paramref name="entityName"/> and <paramref name="attributeName"/>.
    /// For a global option set, provide <paramref name="globalOptionSetName"/>.
    /// </summary>
    Task InsertOptionAsync(
        string? profileName,
        string? entityName,
        string? attributeName,
        string? globalOptionSetName,
        string label,
        int? value,
        CancellationToken ct);

    /// <summary>
    /// Deletes an option value from a local or global option set.
    /// For a local option set, provide <paramref name="entityName"/> and <paramref name="attributeName"/>.
    /// For a global option set, provide <paramref name="globalOptionSetName"/>.
    /// </summary>
    Task DeleteOptionAsync(
        string? profileName,
        string? entityName,
        string? attributeName,
        string? globalOptionSetName,
        int value,
        CancellationToken ct);

    /// <summary>
    /// Lists all global option sets in the environment.
    /// </summary>
    Task<IReadOnlyList<GlobalOptionSetSummaryRecord>> ListGlobalOptionSetsAsync(
        string? profileName,
        CancellationToken ct);
}

/// <summary>
/// Input DTO for a single option (label + optional value) used when creating option sets.
/// </summary>
public sealed record OptionMetadataInput(string Label, int Value);
