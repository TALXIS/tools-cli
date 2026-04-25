namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Authentication identity — the "who". Non-secret fields plus an optional
/// <see cref="SecretRef"/> pointing at OS-vault-held material.
/// </summary>
public sealed class Credential
{
    public string Id { get; set; } = string.Empty;
    public CredentialKind Kind { get; set; }
    public string? Description { get; set; }

    public string? TenantId { get; set; }
    public string? ApplicationId { get; set; }
    public CloudInstance? Cloud { get; set; }
    public string? InteractiveAccountId { get; set; }
    public string? InteractiveUpn { get; set; }

    public string? CertificatePath { get; set; }

    /// <summary>
    /// Optional audience (resource) this credential targets, e.g.
    /// <c>https://org.crm.dynamics.com</c>. When set, scopes can be
    /// validated upfront and requested during interactive login to avoid
    /// deferred consent failures with conditional access policies.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Optional OIDC scopes the credential was consented for. When present,
    /// used for token acquisition instead of deriving scopes dynamically
    /// from the resource URI at runtime. Backward-compatible: existing
    /// credentials without this field continue to work with dynamic scope
    /// resolution.
    /// </summary>
    public string[]? Scopes { get; set; }

    public SecretRef? SecretRef { get; set; }

    /// <summary>When the credential was first persisted.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the credential was last updated (e.g. re-login, secret rotation).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
