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

        var existing = await FindExistingInteractiveCredentialAsync(
            store,
            result,
            cloud,
            explicitAlias,
            ct).ConfigureAwait(false);

        var alias = existing?.Id;
        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = string.IsNullOrWhiteSpace(explicitAlias)
                ? await CredentialAliasResolver.ResolveForUpnAsync(store, result.Upn, ct).ConfigureAwait(false)
                : explicitAlias!.Trim();
        }

        var now = DateTimeOffset.UtcNow;
        var credential = existing ?? new Credential { CreatedAt = now };
        credential.Id = alias;
        credential.Kind = CredentialKind.InteractiveBrowser;
        credential.TenantId = result.TenantId;
        credential.Cloud = cloud;
        credential.ApplicationId = string.IsNullOrWhiteSpace(result.ApplicationId)
            ? credential.ApplicationId
            : result.ApplicationId;
        credential.InteractiveAccountId = string.IsNullOrWhiteSpace(result.AccountId)
            ? credential.InteractiveAccountId
            : result.AccountId;
        credential.InteractiveUpn = result.Upn;
        credential.Description = $"Interactive sign-in ({result.Upn})";
        credential.UpdatedAt = now;

        await store.UpsertAsync(credential, ct).ConfigureAwait(false);
        return new InteractiveCredentialResult(credential, result.Upn, result.TenantId);
    }

    private static async Task<Credential?> FindExistingInteractiveCredentialAsync(
        ICredentialStore store,
        InteractiveLoginResult result,
        CloudInstance cloud,
        string? explicitAlias,
        CancellationToken ct)
    {
        var credentials = await store.ListAsync(ct).ConfigureAwait(false);
        var candidates = credentials
            .Where(c => c.Kind == CredentialKind.InteractiveBrowser)
            .Where(c => string.Equals(c.TenantId, result.TenantId, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Cloud == cloud)
            .Where(c =>
                string.IsNullOrWhiteSpace(result.ApplicationId)
                || string.IsNullOrWhiteSpace(c.ApplicationId)
                || string.Equals(c.ApplicationId, result.ApplicationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(result.AccountId))
        {
            var exact = PickPreferredCandidate(
                candidates.Where(c =>
                    string.Equals(c.InteractiveAccountId, result.AccountId, StringComparison.OrdinalIgnoreCase)),
                explicitAlias,
                result.Upn);
            if (exact is not null)
                return exact;
        }

        // Backward compatibility for older credentials that predate persisted
        // account ids: prefer stored interactive UPN, then legacy "UPN as id".
        return PickPreferredCandidate(
            candidates.Where(c =>
                string.Equals(c.InteractiveUpn, result.Upn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Id, result.Upn, StringComparison.OrdinalIgnoreCase)),
            explicitAlias,
            result.Upn);
    }

    private static Credential? PickPreferredCandidate(
        IEnumerable<Credential> candidates,
        string? explicitAlias,
        string upn)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            var explicitMatch = list.FirstOrDefault(c =>
                string.Equals(c.Id, explicitAlias.Trim(), StringComparison.OrdinalIgnoreCase));
            if (explicitMatch is not null)
                return explicitMatch;
        }

        return list
            .OrderBy(c => string.Equals(c.Id, upn, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .First();
    }
}
