using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Outcome of <see cref="InteractiveCredentialBootstrapper.AcquireAndPersistAsync"/>.
/// </summary>
public sealed record InteractiveCredentialResult(Credential Credential, string Upn, string TenantId);

/// <summary>
/// Single path that performs an interactive browser sign-in and persists
/// the resulting <see cref="Credential"/>. Used both by the primitive
/// <c>auth login</c> command and by the Dataverse one-liner bootstrapper
/// — they must agree on headless policy, alias derivation, and credential
/// shape, so neither duplicates the other.
/// </summary>
public static class InteractiveCredentialBootstrapper
{
    /// <summary>
    /// Enforces the headless policy for interactive browser sign-in,
    /// runs the login, resolves an alias (explicit override or UPN-derived),
    /// and upserts the credential.
    /// </summary>
    public static async Task<InteractiveCredentialResult> AcquireAndPersistAsync(
        IInteractiveLoginService login,
        ICredentialStore store,
        IHeadlessDetector headless,
        string? tenantId,
        CloudInstance cloud,
        string? explicitAlias,
        CancellationToken ct)
    {
        if (login is null) throw new ArgumentNullException(nameof(login));
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (headless is null) throw new ArgumentNullException(nameof(headless));

        headless.EnsureKindAllowed(CredentialKind.InteractiveBrowser);

        var result = await login.LoginAsync(tenantId, cloud, ct).ConfigureAwait(false);

        var alias = string.IsNullOrWhiteSpace(explicitAlias)
            ? await CredentialAliasResolver.ResolveForUpnAsync(store, result.Upn, ct).ConfigureAwait(false)
            : explicitAlias!.Trim();

        var credential = new Credential
        {
            Id = alias,
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = result.TenantId,
            Cloud = cloud,
            Description = $"Interactive sign-in ({result.Upn})",
        };
        await store.UpsertAsync(credential, ct).ConfigureAwait(false);
        return new InteractiveCredentialResult(credential, result.Upn, result.TenantId);
    }
}
