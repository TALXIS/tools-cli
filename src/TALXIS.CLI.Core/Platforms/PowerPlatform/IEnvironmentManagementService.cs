using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A Power Platform environment as surfaced by <c>txc env list</c>. A
/// provider-agnostic projection so the management-plane CLI never depends on
/// control-plane implementation types.
/// </summary>
public sealed record EnvironmentInfo(
    Guid EnvironmentId,
    string DisplayName,
    Uri EnvironmentUrl,
    string? UniqueName,
    Guid? OrganizationId,
    EnvironmentType? EnvironmentType);

/// <summary>
/// User-supplied inputs for <c>txc env create</c>. Raw, human-friendly values
/// (region slug, currency code, language name/LCID, template names) are
/// resolved and validated by the control-plane implementation.
/// </summary>
public sealed record EnvironmentCreateOptions
{
    public string? DisplayName { get; init; }
    public required EnvironmentType EnvironmentType { get; init; }
    public string Region { get; init; } = "unitedstates";
    public string CurrencyCode { get; init; } = "USD";
    public string Language { get; init; } = "1033";
    public string? DomainName { get; init; }
    public IReadOnlyList<string> Templates { get; init; } = Array.Empty<string>();
    public Guid? SecurityGroupId { get; init; }
    public Guid? UserObjectId { get; init; }
    public bool Wait { get; init; }
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(60);
}

/// <summary>
/// Result of an environment creation. When the caller does not wait,
/// <see cref="Completed"/> is <c>false</c> and <see cref="OperationLocation"/>
/// carries the URL that reports provisioning progress.
/// </summary>
public sealed record EnvironmentCreateOutcome(
    Guid? EnvironmentId,
    string? DisplayName,
    Uri? EnvironmentUrl,
    EnvironmentType? EnvironmentType,
    string Status,
    bool Completed,
    Uri? OperationLocation);

/// <summary>
/// User-supplied inputs for <c>txc env update</c>. Only non-null properties
/// are patched — omitted fields are left unchanged on the environment.
/// </summary>
public sealed record EnvironmentUpdateOptions
{
    public required Guid EnvironmentId { get; init; }
    public string? DisplayName { get; init; }
    public EnvironmentType? EnvironmentType { get; init; }
    public Guid? SecurityGroupId { get; init; }
}

/// <summary>
/// Result of an environment update.
/// </summary>
public sealed record EnvironmentUpdateOutcome(
    Guid EnvironmentId,
    string? DisplayName,
    EnvironmentType? EnvironmentType,
    string Status);

/// <summary>
/// Result of an environment deletion. When the caller does not wait,
/// <see cref="Completed"/> is <c>false</c> and <see cref="OperationLocation"/>
/// carries the URL that reports deletion progress.
/// </summary>
public sealed record EnvironmentDeleteOutcome(
    Guid EnvironmentId,
    string Status,
    bool Completed,
    Uri? OperationLocation);

/// <summary>
/// Tenant-level environment administration: listing the environments visible
/// to the active profile's identity, creating new ones, and deleting existing
/// ones. Resolves the (Profile, Connection, Credential) triple internally —
/// the credential and cloud supply the admin authority, independent of any
/// single target environment URL.
/// </summary>
public interface IEnvironmentManagementService
{
    /// <summary>
    /// Lists the Dataverse-backed environments in the tenant visible to the
    /// resolved profile's identity.
    /// </summary>
    Task<IReadOnlyList<EnvironmentInfo>> ListAsync(
        string? profileName,
        CancellationToken ct);

    /// <summary>
    /// Creates a new environment using the resolved profile's credential and
    /// cloud for admin authority.
    /// </summary>
    Task<EnvironmentCreateOutcome> CreateAsync(
        string? profileName,
        EnvironmentCreateOptions options,
        CancellationToken ct);

    /// <summary>
    /// Updates properties of an existing environment. Only the non-null fields
    /// in <paramref name="options"/> are changed.
    /// </summary>
    Task<EnvironmentUpdateOutcome> UpdateAsync(
        string? profileName,
        EnvironmentUpdateOptions options,
        CancellationToken ct);

    /// <summary>
    /// Permanently deletes an environment from the tenant. The BAP admin API
    /// validates that the environment can be deleted before initiating the
    /// operation. By default returns immediately; pass <paramref name="wait"/>
    /// to block until deletion completes.
    /// </summary>
    Task<EnvironmentDeleteOutcome> DeleteAsync(
        string? profileName,
        Guid environmentId,
        bool wait,
        TimeSpan maxWait,
        CancellationToken ct);
}
