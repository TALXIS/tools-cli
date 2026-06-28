using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Single path that performs a device code sign-in and persists the resulting
/// <see cref="Credential"/>. Mirrors <see cref="InteractiveCredentialBootstrapper"/>
/// but uses <see cref="IDeviceCodeLoginService"/> and stores the credential as
/// <see cref="CredentialKind.DeviceCode"/>.
/// </summary>
/// <remarks>
/// Device code credentials share the same silent-renewal path as interactive
/// browser credentials — MSAL's token cache holds both refresh token types
/// identically. The credential kind is tracked so the CLI knows which
/// re-authentication flow to use when <c>MsalUiRequiredException</c> fires.
/// </remarks>
public static class DeviceCodeCredentialBootstrapper
{
    /// <summary>
    /// Enforces the headless policy for device code sign-in, runs the
    /// login, resolves an alias (explicit override or UPN-derived), and
    /// upserts the credential.
    /// </summary>
    public static async Task<InteractiveCredentialResult> AcquireAndPersistAsync(
        IDeviceCodeLoginService login,
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

        // Device code still requires a TTY for the user to see the code
        // and confirm — it is not permitted in fully headless (no-TTY) mode.
        headless.EnsureKindAllowed(CredentialKind.DeviceCode);

        var result = await login.LoginAsync(tenantId, cloud, ct).ConfigureAwait(false);

        var existing = await FindExistingDeviceCodeCredentialAsync(
            store, result, cloud, explicitAlias, ct).ConfigureAwait(false);

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
        credential.Kind = CredentialKind.DeviceCode;
        credential.TenantId = result.TenantId;
        credential.Cloud = cloud;
        credential.ApplicationId = string.IsNullOrWhiteSpace(result.ApplicationId)
            ? credential.ApplicationId
            : result.ApplicationId;
        credential.InteractiveAccountId = string.IsNullOrWhiteSpace(result.AccountId)
            ? credential.InteractiveAccountId
            : result.AccountId;
        credential.InteractiveUpn = result.Upn;
        credential.Description = $"Device code sign-in ({result.Upn})";
        credential.UpdatedAt = now;

        await store.UpsertAsync(credential, ct).ConfigureAwait(false);
        return new InteractiveCredentialResult(credential, result.Upn, result.TenantId);
    }

    private static async Task<Credential?> FindExistingDeviceCodeCredentialAsync(
        ICredentialStore store,
        InteractiveLoginResult result,
        CloudInstance cloud,
        string? explicitAlias,
        CancellationToken ct)
    {
        var credentials = await store.ListAsync(ct).ConfigureAwait(false);
        // Match existing credentials of either DeviceCode or InteractiveBrowser
        // kind — a user re-logging from Codespaces should reuse their existing
        // credential entry rather than creating a duplicate.
        var candidates = credentials
            .Where(c => c.Kind is CredentialKind.DeviceCode or CredentialKind.InteractiveBrowser)
            .Where(c => string.Equals(c.TenantId, result.TenantId, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Cloud == cloud)
            .Where(c =>
                string.IsNullOrWhiteSpace(result.ApplicationId)
                || string.IsNullOrWhiteSpace(c.ApplicationId)
                || string.Equals(c.ApplicationId, result.ApplicationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(result.AccountId))
        {
            var exact = candidates.FirstOrDefault(c =>
                string.Equals(c.InteractiveAccountId, result.AccountId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        // Fall back to UPN or legacy id-based matching.
        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            var explicitMatch = candidates.FirstOrDefault(c =>
                string.Equals(c.Id, explicitAlias.Trim(), StringComparison.OrdinalIgnoreCase));
            if (explicitMatch is not null)
                return explicitMatch;
        }

        return candidates.FirstOrDefault(c =>
            string.Equals(c.InteractiveUpn, result.Upn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Id, result.Upn, StringComparison.OrdinalIgnoreCase));
    }
}
