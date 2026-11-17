using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>
/// Registers the endpoint-default managed identity without acquiring a
/// resource-specific token. Shared by credential login and profile setup.
/// </summary>
public static class ManagedIdentityCredentialBootstrapper
{
    public static async Task<Credential> RegisterAsync(
        ICredentialStore store,
        IHeadlessDetector headless,
        string? tenantId,
        CloudInstance cloud,
        string? explicitAlias,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(headless);
        headless.EnsureKindAllowed(CredentialKind.ManagedIdentity);

        var tenant = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
        Credential? existing;
        string alias;
        if (explicitAlias is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(explicitAlias);
            alias = explicitAlias.Trim();
            existing = await store.GetAsync(alias, ct).ConfigureAwait(false);
            if (existing is not null && existing.Kind != CredentialKind.ManagedIdentity)
                throw new InvalidOperationException(
                    $"Credential '{alias}' already uses {existing.Kind}. Choose a different --alias for managed identity.");
        }
        else
        {
            existing = (await store.ListAsync(ct).ConfigureAwait(false))
                .Where(c => c.Kind == CredentialKind.ManagedIdentity
                    && string.IsNullOrWhiteSpace(c.ApplicationId)
                    && (c.Cloud ?? CloudInstance.Public) == cloud
                    && string.Equals(c.TenantId, tenant, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            alias = existing?.Id ?? await CredentialAliasResolver.ResolveFreeNameAsync(
                "managed-identity",
                async (candidate, token) => await store.GetAsync(candidate, token).ConfigureAwait(false) is not null,
                ct).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var credential = new Credential
        {
            Id = alias,
            Kind = CredentialKind.ManagedIdentity,
            TenantId = tenant,
            Cloud = cloud,
            Description = existing?.Description ?? "Endpoint-default managed identity",
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };
        await store.UpsertAsync(credential, ct).ConfigureAwait(false);
        return credential;
    }
}
