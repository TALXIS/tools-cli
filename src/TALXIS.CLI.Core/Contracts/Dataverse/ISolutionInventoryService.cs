namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Installed-solution row returned by <see cref="ISolutionInventoryService.ListAsync"/>.
/// Lives in Core so feature commands can bind to it without referencing the
/// Dataverse platform implementation project.
/// </summary>
public sealed record InstalledSolutionRecord(
    Guid Id,
    string UniqueName,
    string? FriendlyName,
    string? Version,
    bool Managed);

/// <summary>
/// Dataverse solution inventory operations. Implemented by the Dataverse
/// platform adapter; consumed by thin feature commands.
/// </summary>
public interface ISolutionInventoryService
{
    /// <summary>
    /// Lists installed solutions in the environment referenced by
    /// <paramref name="profileName"/>. Handles profile resolution and
    /// connection lifetime internally.
    /// </summary>
    Task<IReadOnlyList<InstalledSolutionRecord>> ListAsync(
        string? profileName,
        bool? managedFilter,
        CancellationToken ct);
}
