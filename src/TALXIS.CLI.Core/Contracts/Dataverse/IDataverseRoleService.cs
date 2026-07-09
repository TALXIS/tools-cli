namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Read-only Dataverse role lookup service.
/// </summary>
public interface IDataverseRoleService
{
    /// <summary>
    /// Lists Dataverse roles. When <paramref name="filter"/> is supplied, it
    /// is applied as a name contains-filter.
    /// </summary>
    Task<IReadOnlyList<DataverseRoleRecord>> ListAsync(
        string? profileName,
        string? filter,
        CancellationToken ct);

    /// <summary>
    /// Resolves a single Dataverse role by role GUID or exact role name.
    /// Returns <c>null</c> when no record matches. Throws
    /// <see cref="DataverseAmbiguousMatchException"/> when the role name is
    /// duplicated across multiple business units.
    /// </summary>
    Task<DataverseRoleRecord?> GetAsync(
        string? profileName,
        string nameOrGuid,
        CancellationToken ct);
}
