using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Contracts.PowerPlatform;

/// <summary>
/// A tenant-level "folder" that organizes managed environments and serves as
/// the attachment point for governance rules and role assignments. Mirrors
/// Microsoft's own "Environment groups" concept
/// (<c>https://learn.microsoft.com/en-us/power-platform/admin/environment-groups</c>).
/// </summary>
public sealed record PowerPlatformEnvironmentGroup(
    Guid Id,
    string DisplayName,
    string? Description,
    DateTimeOffset? CreatedOn,
    Guid? CreatedByPrincipalObjectId,
    DateTimeOffset? LastModifiedOn,
    IReadOnlyList<Guid> EnvironmentIds);

/// <summary>
/// Fields accepted when creating a new environment group.
/// </summary>
public sealed record PowerPlatformEnvironmentGroupCreateOptions(
    string DisplayName,
    string? Description);

/// <summary>
/// Fields accepted when updating an existing environment group. Only
/// non-null members are sent to the API (partial update).
/// </summary>
public sealed record PowerPlatformEnvironmentGroupUpdateOptions(
    string? DisplayName,
    string? Description);

/// <summary>
/// Client abstraction over the environment-group management endpoints under
/// <c>api.powerplatform.com/environmentmanagement/environmentGroups</c>.
/// Membership operations (<see cref="AddEnvironmentAsync"/>/
/// <see cref="RemoveEnvironmentAsync"/>) are asynchronous on the service side
/// (<c>202 Accepted</c>); implementations poll the returned operation to
/// completion before returning.
/// </summary>
public interface IPowerPlatformEnvironmentGroupClient
{
    Task<IReadOnlyList<PowerPlatformEnvironmentGroup>> ListAsync(
        Connection connection, Credential credential, CancellationToken ct);

    Task<PowerPlatformEnvironmentGroup?> GetAsync(
        Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct);

    Task<PowerPlatformEnvironmentGroup> CreateAsync(
        Connection connection, Credential credential, PowerPlatformEnvironmentGroupCreateOptions options, CancellationToken ct);

    Task<PowerPlatformEnvironmentGroup> UpdateAsync(
        Connection connection, Credential credential, Guid environmentGroupId, PowerPlatformEnvironmentGroupUpdateOptions options, CancellationToken ct);

    /// <summary>
    /// Deletes the environment group. The API returns <c>409 Conflict</c>
    /// when the group still has member environments or assigned policies;
    /// callers wanting the CLI's <c>--force</c> cascading-delete behavior
    /// should remove members/assignments first (see
    /// <c>envgroup-policy-force-delete</c> follow-up work) rather than rely
    /// on this method to cascade.
    /// </summary>
    Task DeleteAsync(
        Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct);

    Task AddEnvironmentAsync(
        Connection connection, Credential credential, Guid environmentGroupId, Guid environmentId, CancellationToken ct);

    Task RemoveEnvironmentAsync(
        Connection connection, Credential credential, Guid environmentGroupId, Guid environmentId, CancellationToken ct);
}
