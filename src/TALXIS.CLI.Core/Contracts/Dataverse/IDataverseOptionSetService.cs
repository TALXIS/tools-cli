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
    /// Inserts a new option value into an option set.
    /// For a local option set, provide <paramref name="entityName"/> and <paramref name="attributeName"/>.
    /// For a global option set, provide <paramref name="optionSetName"/>.
    /// </summary>
    Task InsertOptionAsync(
        string? profileName,
        string? entityName,
        string? attributeName,
        string? optionSetName,
        string label,
        int? value,
        CancellationToken ct);

    /// <summary>
    /// Deletes an option value from an option set.
    /// For a local option set, provide <paramref name="entityName"/> and <paramref name="attributeName"/>.
    /// For a global option set, provide <paramref name="optionSetName"/>.
    /// </summary>
    Task DeleteOptionAsync(
        string? profileName,
        string? entityName,
        string? attributeName,
        string? optionSetName,
        int value,
        CancellationToken ct);

    /// <summary>
    /// Deletes an existing global option set from Dataverse by schema name.
    /// </summary>
    Task DeleteGlobalOptionSetAsync(
        string? profileName,
        string optionSetName,
        CancellationToken ct);

    /// <summary>
    /// Lists all global option sets in the environment.
    /// </summary>
    Task<IReadOnlyList<GlobalOptionSetSummaryRecord>> ListGlobalOptionSetsAsync(
        string? profileName,
        CancellationToken ct);

    /// <summary>
    /// Describes a specific global option set — returns its options (value + label pairs).
    /// </summary>
    /// <param name="languageCode">Optional LCID for label language (e.g. 1033=English, 1029=Czech). Null = user's language.</param>
    Task<GlobalOptionSetDetailRecord> DescribeGlobalOptionSetAsync(
        string? profileName,
        string optionSetName,
        int? languageCode,
        CancellationToken ct);
}

/// <summary>
/// Input DTO for a single option (label + optional value) used when creating option sets.
/// </summary>
public sealed record OptionMetadataInput(string Label, int Value)
{
    /// <summary>
    /// Parses a comma-separated options string into <see cref="OptionMetadataInput"/> items.
    /// Supports "Label:Value" pairs or plain "Label" (auto-valued starting at 100000000).
    /// </summary>
    public static OptionMetadataInput[] ParseCsv(string csv)
    {
        var entries = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
            return Array.Empty<OptionMetadataInput>();

        var results = new OptionMetadataInput[entries.Length];
        int autoValue = 100_000_000;

        for (int i = 0; i < entries.Length; i++)
        {
            var parts = entries[i].Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int v))
            {
                results[i] = new OptionMetadataInput(parts[0].Trim(), v);
            }
            else
            {
                results[i] = new OptionMetadataInput(parts[0].Trim(), autoValue++);
            }
        }

        return results;
    }
};

/// <summary>
/// Detail record for a global option set, including all option values and labels.
/// </summary>
public sealed record GlobalOptionSetDetailRecord(
    string Name,
    string? DisplayName,
    string? Description,
    string OptionSetType,
    bool IsCustomOptionSet,
    IReadOnlyList<OptionValueRecord> Options);

/// <summary>
/// A single option value within an option set.
/// </summary>
public sealed record OptionValueRecord(int Value, string? Label, string? Description);
