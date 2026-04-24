using System.Security.Cryptography.X509Certificates;

namespace TALXIS.CLI.Core.Identity;

/// <summary>
/// Callback that returns a client assertion (JWT) for federated credential
/// flows (GitHub OIDC, ADO Workload Identity). Registered per-credential
/// when the Credential's <see cref="Core.Model.CredentialKind"/> is
/// <see cref="Core.Model.CredentialKind.WorkloadIdentityFederation"/>.
/// </summary>
public delegate Task<string> ClientAssertionCallback(CancellationToken ct);

/// <summary>
/// Inputs required to rehydrate a confidential-client secret or certificate
/// from <see cref="Core.Abstractions.ICredentialVault"/> at build time.
/// </summary>
public sealed record ConfidentialClientMaterial
{
    public string? ClientSecret { get; init; }
    public X509Certificate2? Certificate { get; init; }
    public ClientAssertionCallback? AssertionCallback { get; init; }

    public bool IsEmpty =>
        string.IsNullOrEmpty(ClientSecret) && Certificate is null && AssertionCallback is null;
}
