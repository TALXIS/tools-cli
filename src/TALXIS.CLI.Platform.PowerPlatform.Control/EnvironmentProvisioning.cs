using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// User-supplied inputs for creating a Power Platform environment. Raw,
/// human-friendly values (region slug, currency code, language name/LCID,
/// template names) are resolved and validated against the BAP per-region
/// catalogs by the provisioner before the create request is issued.
/// </summary>
public sealed record EnvironmentCreateRequest
{
    /// <summary>Display name. Required for every type except <see cref="EnvironmentType.Teams"/>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Lifecycle type / SKU. <see cref="EnvironmentType.Default"/> is not creatable.</summary>
    public required EnvironmentType EnvironmentType { get; init; }

    /// <summary>Azure geo region slug (e.g. <c>unitedstates</c>, <c>europe</c>).</summary>
    public string Region { get; init; } = "unitedstates";

    /// <summary>ISO currency code (validated against the region's catalog).</summary>
    public string CurrencyCode { get; init; } = "USD";

    /// <summary>Localized language name (e.g. <c>English (United States)</c>) or raw LCID (e.g. <c>1033</c>).</summary>
    public string Language { get; init; } = "1033";

    /// <summary>Optional subdomain for the environment URL (2–32 chars).</summary>
    public string? DomainName { get; init; }

    /// <summary>Optional Dynamics 365 app template names to provision (validated against the region/SKU catalog).</summary>
    public IReadOnlyList<string> Templates { get; init; } = Array.Empty<string>();

    /// <summary>Optional Entra security group that gates membership. Required for <see cref="EnvironmentType.Teams"/>.</summary>
    public Guid? SecurityGroupId { get; init; }

    /// <summary>Owning user (Entra object id) — only valid for <see cref="EnvironmentType.Developer"/> environments.</summary>
    public Guid? UserObjectId { get; init; }

    /// <summary>Whether to poll until provisioning completes (otherwise returns after queueing).</summary>
    public bool Wait { get; init; }

    /// <summary>Maximum time to wait when <see cref="Wait"/> is set. Mirrors PAC's 60-minute cap.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(60);
}

/// <summary>
/// Outcome of an environment creation request. When the caller does not wait,
/// <see cref="Completed"/> is <c>false</c> and <see cref="OperationLocation"/>
/// carries the URL that reports provisioning progress.
/// </summary>
public sealed record EnvironmentCreateResult(
    Guid? EnvironmentId,
    string? DisplayName,
    Uri? EnvironmentUrl,
    TALXIS.CLI.Core.Model.EnvironmentType? EnvironmentType,
    string Status,
    bool Completed,
    Uri? OperationLocation);

/// <summary>
/// Creates Power Platform environments through the BAP admin API, including
/// the per-region currency/language/template validation lookups and async
/// provisioning polling.
/// </summary>
public interface IPowerPlatformEnvironmentProvisioner
{
    Task<EnvironmentCreateResult> CreateAsync(
        Connection connection,
        Credential credential,
        EnvironmentCreateRequest request,
        CancellationToken ct);
}
