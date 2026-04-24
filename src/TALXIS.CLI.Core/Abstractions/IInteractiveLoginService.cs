using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Result of an interactive browser sign-in. The token itself stays in
/// the MSAL token cache; only the account identity leaves this boundary
/// so <c>config auth login</c> can persist a matching
/// <see cref="Credential"/>.
/// </summary>
/// <param name="Upn">User principal name returned by the identity provider.</param>
/// <param name="TenantId">Tenant that issued the sign-in.</param>
/// <param name="AccountId">
/// Stable account identifier within the provider/cache boundary (for MSAL,
/// typically <c>HomeAccountId.Identifier</c>).
/// </param>
/// <param name="ApplicationId">
/// Client/application id that owns the interactive cache entry. This keeps
/// same-user dedupe scoped to the same provider/app boundary.
/// </param>
public sealed record InteractiveLoginResult(
    string Upn,
    string TenantId,
    string? AccountId = null,
    string? ApplicationId = null);

/// <summary>
/// Performs an eager interactive browser sign-in against Entra, primes the
/// MSAL token cache, and returns the signed-in account identity.
/// </summary>
/// <remarks>
/// In v1 this is Dataverse-flavoured (pinned pac public client id + its
/// sovereign authority map). When further providers land, each provider
/// registers its own implementation keyed by <see cref="ProviderKind"/>.
/// </remarks>
public interface IInteractiveLoginService
{
    /// <param name="tenantId">
    /// Optional Entra tenant id or domain. When <c>null</c>, the
    /// <c>organizations</c> endpoint is used and the tenant is inferred
    /// from whichever account the user picks in the browser.
    /// </param>
    /// <param name="cloud">Sovereign cloud for the authority URL.</param>
    Task<InteractiveLoginResult> LoginAsync(
        string? tenantId,
        CloudInstance cloud,
        CancellationToken ct);
}
