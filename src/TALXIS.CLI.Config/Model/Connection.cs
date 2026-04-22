using System.Text.Json;
using System.Text.Json.Serialization;

namespace TALXIS.CLI.Config.Model;

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

    // Dataverse
    public string? EnvironmentUrl { get; set; }
    public string? OrganizationId { get; set; }
    public CloudInstance? Cloud { get; set; }
    public string? TenantId { get; set; }

    // Azure
    public string? SubscriptionId { get; set; }

    // ADO
    public string? Organization { get; set; }
    public string? Project { get; set; }

    // Jira
    public string? ServerUrl { get; set; }

    /// <summary>Captured but unprocessed fields (forward-compat).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
