namespace TALXIS.CLI.Config.Model;

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

    public string? CertificatePath { get; set; }

    public SecretRef? SecretRef { get; set; }
}
