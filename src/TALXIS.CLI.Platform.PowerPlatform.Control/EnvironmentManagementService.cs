using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Profile-resolving orchestrator for tenant-level environment administration.
/// Mirrors <see cref="EnvironmentSettingsService"/>: it owns the
/// (Profile, Connection, Credential) resolution and delegates the BAP admin
/// API work to the reusable catalog (list) and provisioner (create). The
/// connection's credential and cloud supply the admin authority — the target
/// environment URL is irrelevant for these tenant-scoped operations.
/// </summary>
public sealed class EnvironmentManagementService : IEnvironmentManagementService
{
    private readonly IConfigurationResolver _resolver;
    private readonly ICredentialStore _credentials;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly IPowerPlatformEnvironmentProvisioner _provisioner;

    public EnvironmentManagementService(
        IConfigurationResolver resolver,
        ICredentialStore credentials,
        IPowerPlatformEnvironmentCatalog catalog,
        IPowerPlatformEnvironmentProvisioner provisioner)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
    }

    public async Task<IReadOnlyList<EnvironmentInfo>> ListAsync(
        string? profileName,
        string? credentialId,
        CloudInstance? cloud,
        CancellationToken ct)
    {
        var (connection, credential) = await ResolveAuthorityAsync(profileName, credentialId, cloud, ct).ConfigureAwait(false);
        var environments = await _catalog.ListAsync(connection, credential, ct).ConfigureAwait(false);

        return environments
            .Select(e => new EnvironmentInfo(
                e.EnvironmentId,
                e.DisplayName,
                e.EnvironmentUrl,
                e.UniqueName,
                e.OrganizationId,
                e.EnvironmentType))
            .ToList();
    }

    public async Task<EnvironmentCreateOutcome> CreateAsync(
        string? profileName,
        EnvironmentCreateOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (connection, credential) = await ResolveAuthorityAsync(
            profileName, options.CredentialId, options.Cloud, ct).ConfigureAwait(false);

        var request = new EnvironmentCreateRequest
        {
            DisplayName = options.DisplayName,
            EnvironmentType = options.EnvironmentType,
            Region = options.Region,
            CurrencyCode = options.CurrencyCode,
            Language = options.Language,
            DomainName = options.DomainName,
            Templates = options.Templates,
            SecurityGroupId = options.SecurityGroupId,
            UserObjectId = options.UserObjectId,
            Wait = options.Wait,
            MaxWait = options.MaxWait,
        };

        var result = await _provisioner.CreateAsync(connection, credential, request, ct).ConfigureAwait(false);

        return new EnvironmentCreateOutcome(
            result.EnvironmentId,
            result.DisplayName,
            result.EnvironmentUrl,
            result.EnvironmentType,
            result.Status,
            result.Completed,
            result.OperationLocation);
    }

    public async Task<EnvironmentUpdateOutcome> UpdateAsync(
        string? profileName,
        EnvironmentUpdateOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);

        var request = new EnvironmentUpdateRequest
        {
            EnvironmentId = options.EnvironmentId,
            DisplayName = options.DisplayName,
            EnvironmentType = options.EnvironmentType,
            SecurityGroupId = options.SecurityGroupId,
        };

        var result = await _provisioner.UpdateAsync(ctx.Connection, ctx.Credential, request, ct).ConfigureAwait(false);

        return new EnvironmentUpdateOutcome(
            result.EnvironmentId,
            result.DisplayName,
            result.EnvironmentType,
            result.Status);
    }

    public async Task<EnvironmentDeleteOutcome> DeleteAsync(
        string? profileName,
        Guid environmentId,
        bool wait,
        TimeSpan maxWait,
        CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var result = await _provisioner.DeleteAsync(ctx.Connection, ctx.Credential, environmentId, wait, maxWait, ct).ConfigureAwait(false);

        return new EnvironmentDeleteOutcome(
            result.EnvironmentId,
            result.Status,
            result.Completed,
            result.OperationLocation);
    }

    private async Task<(Connection Connection, Credential Credential)> ResolveAuthorityAsync(
        string? profileName,
        string? credentialId,
        CloudInstance? cloud,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(credentialId) || cloud is not null)
        {
            var credential = await ResolveCredentialAsync(credentialId, cloud, ct).ConfigureAwait(false);
            return (CreateTenantConnection(credential, cloud), credential);
        }

        try
        {
            var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
            return (ctx.Connection, ctx.Credential);
        }
        catch (ConfigurationResolutionException ex) when (
            string.IsNullOrWhiteSpace(profileName)
            && ex.Message.StartsWith("No txc profile could be resolved", StringComparison.Ordinal))
        {
            var credential = await ResolveCredentialAsync(null, null, ct).ConfigureAwait(false);
            return (CreateTenantConnection(credential, null), credential);
        }
    }

    private async Task<Credential> ResolveCredentialAsync(
        string? credentialId,
        CloudInstance? cloud,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(credentialId))
        {
            var credential = await _credentials.GetAsync(credentialId.Trim(), ct).ConfigureAwait(false);
            if (credential is null)
                throw new ConfigurationResolutionException(
                    $"Credential '{credentialId}' was not found. Run 'txc config auth list' to see available credentials.");

            if (cloud is not null && credential.Cloud is not null && credential.Cloud != cloud)
                throw new ConfigurationResolutionException(
                    $"Credential '{credential.Id}' is for cloud '{credential.Cloud}', not '{cloud}'. Omit --cloud or choose a matching credential.");

            return credential;
        }

        var candidates = (await _credentials.ListAsync(ct).ConfigureAwait(false))
            .Where(IsSupportedTenantCredential)
            .Where(c => cloud is null || c.Cloud is null || c.Cloud == cloud)
            .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new ConfigurationResolutionException(
                "No txc profile could be resolved and no auth credential is available. Run 'txc config auth login' first, or pass --profile."),
            _ => throw new ConfigurationResolutionException(
                "No txc profile could be resolved and multiple auth credentials are available. Pass --auth <credential> to select one."),
        };
    }

    private static Connection CreateTenantConnection(Credential credential, CloudInstance? cloud)
        => new()
        {
            Id = $"auth:{credential.Id}",
            Provider = ProviderKind.Dataverse,
            Cloud = cloud ?? credential.Cloud ?? CloudInstance.Public,
            TenantId = credential.TenantId,
            Description = "Tenant-level Power Platform admin authority",
        };

    private static bool IsSupportedTenantCredential(Credential credential)
        => credential.Kind is CredentialKind.InteractiveBrowser
            or CredentialKind.DeviceCode
            or CredentialKind.ClientSecret
            or CredentialKind.ClientCertificate
            or CredentialKind.WorkloadIdentityFederation;
}
