using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Service endpoint metadata — the "where". Provider-specific fields are
/// carried as optional typed properties; unknown future fields land in
/// <see cref="ExtraFields"/> to survive round-trips without losing data.
/// </summary>
public sealed class Connection
{
    public string Id { get; set; } = string.Empty;
    public ProviderKind Provider { get; set; }
    public string? Description { get; set; }

    // Dataverse (the only provider implemented in v1 — Azure / ADO / Jira
    // fields are intentionally not on this model until their providers land;
    // ExtraFields below round-trips any unknown future keys without loss).
    public string? EnvironmentUrl { get; set; }
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Power Platform environment GUID used by the control plane API
    /// (<c>api.powerplatform.com/environmentmanagement/environments/{id}</c>).
    /// Populated during live-check or explicitly via <c>--environment-id</c>.
    /// </summary>
    public Guid? EnvironmentId { get; set; }

    public CloudInstance? Cloud { get; set; }
    public string? TenantId { get; set; }

    /// <summary>
    /// Human-readable environment display name from the Power Platform catalog.
    /// Populated during bootstrap or live-check. Used as the primary source for
    /// connection/profile slug derivation.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Power Platform environment lifecycle type (<c>Production</c>,
    /// <c>Sandbox</c>, <c>Trial</c>, <c>Developer</c>, <c>Default</c>).
    /// Resolved from the Power Platform admin API's
    /// <c>properties.environmentSku</c> field. When null, destructive
    /// operations treat the environment as Production (fail-safe).
    /// Can be overridden via <c>--environment-type</c> on connection create.
    /// </summary>
    public EnvironmentType? EnvironmentType { get; set; }

    /// <summary>When the connection was first persisted.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the connection was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Captured but unprocessed fields (forward-compat).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
