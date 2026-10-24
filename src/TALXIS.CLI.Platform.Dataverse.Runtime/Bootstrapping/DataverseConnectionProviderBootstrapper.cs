using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Platform.Dataverse.Runtime.Authority;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.Bootstrapping;

/// <summary>
/// Dataverse implementation of <see cref="IConnectionProviderBootstrapper"/>.
/// Drives: sign-in (<see cref="CredentialSignInService"/> picks browser or device code)
/// → credential alias resolve + upsert → connection upsert. Called by
/// <c>profile create --url</c>.
/// </summary>
public sealed class DataverseConnectionProviderBootstrapper : IConnectionProviderBootstrapper
{
    private readonly CredentialSignInService _signIn;
    private readonly ICredentialStore _credentials;
    private readonly ConnectionUpsertService _connectionUpserts;
    private readonly IConnectionStore _connectionStore;
    private readonly IProfileStore _profiles;
    private readonly IPowerPlatformEnvironmentCatalog _environmentCatalog;
    private readonly ILogger<DataverseConnectionProviderBootstrapper> _logger;

    public DataverseConnectionProviderBootstrapper(
        CredentialSignInService signIn,
        ICredentialStore credentials,
        ConnectionUpsertService connections,
        IConnectionStore connectionStore,
        IProfileStore profiles,
        IPowerPlatformEnvironmentCatalog environmentCatalog,
        ILogger<DataverseConnectionProviderBootstrapper> logger)
    {
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _connectionUpserts = connections ?? throw new ArgumentNullException(nameof(connections));
        _connectionStore = connectionStore ?? throw new ArgumentNullException(nameof(connectionStore));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _environmentCatalog = environmentCatalog ?? throw new ArgumentNullException(nameof(environmentCatalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ProviderKind Provider => ProviderKind.Dataverse;

    public async Task<ProfileBootstrapResult> BootstrapAsync(
        ProfileBootstrapRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var environmentUrl)
            || (environmentUrl.Scheme != Uri.UriSchemeHttp && environmentUrl.Scheme != Uri.UriSchemeHttps))
        {
            return new ProfileBootstrapResult(
                string.Empty,
                null,
                null,
                null,
                $"'{request.EnvironmentUrl}' is not an absolute http(s) URL.");
        }

        var cloud = request.Cloud
            ?? DataverseCloudMap.TryInferFromEnvironmentUrl(environmentUrl)
            ?? CloudInstance.Public;

        _logger.LogInformation("Signing in for '{Url}'...", request.EnvironmentUrl);
        var acquired = (await _signIn.SignInAsync(
            request.TenantId, cloud, explicitAlias: null, forceDeviceCode: false, ct).ConfigureAwait(false)).Credential;

        var names = await ResolveBindingNamesAsync(request, environmentUrl, cloud, acquired, ct).ConfigureAwait(false);
        if (names is null)
        {
            return new ProfileBootstrapResult(
                string.Empty,
                acquired.Credential,
                null,
                acquired.Upn,
                $"Cannot derive a profile name from environment '{request.EnvironmentUrl}'. Pass --name explicitly.");
        }

        // Now that the environment URL is confirmed, capture the audience
        // and scopes on the credential so token acquisition can use them
        // and diagnostics can report what the credential was consented for.
        if (Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var envUriForScopes))
        {
            var authority = envUriForScopes.GetLeftPart(UriPartial.Authority);
            if (string.IsNullOrEmpty(acquired.Credential.Audience))
            {
                acquired.Credential.Audience = authority;
                acquired.Credential.Scopes = new[] { authority + "//.default" };
                await _credentials.UpsertAsync(acquired.Credential, ct).ConfigureAwait(false);
            }
        }

        var upsert = await _connectionUpserts.ValidateAndUpsertAsync(
            names.ConnectionName,
            request.Provider,
            request.EnvironmentUrl,
            cloud,
            organizationId: names.Environment.OrganizationId?.ToString(),
            environmentId: names.Environment.EnvironmentId?.ToString(),
            tenantId: request.TenantId ?? acquired.TenantId,
            description: request.Description,
            ct,
            displayName: names.Environment.DisplayName,
            environmentType: names.Environment.EnvironmentType).ConfigureAwait(false);

        if (upsert.Error is not null)
            return new ProfileBootstrapResult(names.ProfileName, acquired.Credential, null, acquired.Upn, upsert.Error);

        return new ProfileBootstrapResult(names.ProfileName, acquired.Credential, upsert.Connection, acquired.Upn, null);
    }

    private sealed record EnvironmentMetadata(
        Guid? EnvironmentId,
        string? DisplayName,
        EnvironmentType? EnvironmentType,
        Guid? OrganizationId);

    private sealed record BindingNames(
        string ProfileName,
        string ConnectionName,
        EnvironmentMetadata Environment);

    private async Task<BindingNames?> ResolveBindingNamesAsync(
        ProfileBootstrapRequest request,
        Uri environmentUrl,
        CloudInstance cloud,
        InteractiveCredentialResult acquired,
        CancellationToken ct)
    {
        var explicitName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var existingConnection = await FindExistingConnectionAsync(request, cloud, acquired, ct).ConfigureAwait(false);
        var existingProfile = existingConnection is null
            ? null
            : await FindExistingProfileAsync(existingConnection.Id, acquired.Credential.Id, ct).ConfigureAwait(false);

        // Look up environment metadata on every path, not only when deriving a
        // name, so the guard can tell the environment type.
        var environment = await TryGetEnvironmentAsync(request, environmentUrl, cloud, acquired, ct).ConfigureAwait(false);

        // Fall back to stored metadata so a failed lookup does not wipe it.
        Guid? organizationId = environment?.OrganizationId;
        if (organizationId is null && Guid.TryParse(existingConnection?.OrganizationId, out var existingOrgId))
            organizationId = existingOrgId;

        var metadata = new EnvironmentMetadata(
            environment?.EnvironmentId ?? existingConnection?.EnvironmentId,
            environment?.DisplayName ?? existingConnection?.DisplayName,
            environment?.EnvironmentType ?? existingConnection?.EnvironmentType,
            organizationId);

        if (!string.IsNullOrEmpty(explicitName))
        {
            return new BindingNames(explicitName, existingConnection?.Id ?? explicitName, metadata);
        }

        if (existingProfile is not null)
        {
            return new BindingNames(existingProfile.Id, existingProfile.ConnectionRef!, metadata);
        }

        string? preferredBase = null;
        if (environment is not null)
        {
            // Include tenant domain in the slug so multi-customer users can
            // distinguish environments across tenants at a glance.
            var tenantDomain = CredentialAliasResolver.ExtractTenantShortName(acquired.Upn);
            preferredBase = ProviderUrlResolver.DeriveDefaultName(environment.DisplayName, request.EnvironmentUrl, tenantDomain);
        }

        if (string.IsNullOrEmpty(preferredBase))
        {
            var tenantDomain = CredentialAliasResolver.ExtractTenantShortName(acquired.Upn);
            preferredBase = ProviderUrlResolver.DeriveDefaultName(null, request.EnvironmentUrl, tenantDomain);
        }
        if (string.IsNullOrEmpty(preferredBase))
            return null;

        if (existingConnection is not null)
        {
            var profileName = await CredentialAliasResolver.ResolveFreeNameAsync(
                preferredBase,
                async (candidate, existsCt) =>
                    await _profiles.GetAsync(candidate, existsCt).ConfigureAwait(false) is not null,
                ct).ConfigureAwait(false);

            return new BindingNames(profileName, existingConnection.Id, metadata);
        }

        // Keep profile + connection names aligned so the mental model is
        // "one name, one profile, one connection" in the quickstart flow.
        var sharedName = await CredentialAliasResolver.ResolveFreeNameAsync(
            preferredBase,
            async (candidate, existsCt) =>
                await _profiles.GetAsync(candidate, existsCt).ConfigureAwait(false) is not null
                || await _connectionStore.GetAsync(candidate, existsCt).ConfigureAwait(false) is not null,
            ct).ConfigureAwait(false);

        return new BindingNames(sharedName, sharedName, metadata);
    }

    private async Task<PowerPlatformEnvironmentSummary?> TryGetEnvironmentAsync(
        ProfileBootstrapRequest request,
        Uri environmentUrl,
        CloudInstance cloud,
        InteractiveCredentialResult acquired,
        CancellationToken ct)
    {
        var ephemeralConnection = new Connection
        {
            Id = "(ephemeral)",
            Provider = request.Provider,
            EnvironmentUrl = environmentUrl.ToString().TrimEnd('/'),
            Cloud = cloud,
            TenantId = request.TenantId ?? acquired.TenantId,
        };

        try
        {
            return await _environmentCatalog
                .TryGetByEnvironmentUrlAsync(ephemeralConnection, acquired.Credential, environmentUrl, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Best-effort: any failure except caller cancellation falls back to stored metadata.
            _logger.LogWarning(
                ex,
                "Could not resolve Power Platform environment metadata for '{Url}'. Continuing with the metadata already stored on the connection, if any.",
                request.EnvironmentUrl);
            return null;
        }
    }

    private async Task<Connection?> FindExistingConnectionAsync(
        ProfileBootstrapRequest request,
        CloudInstance cloud,
        InteractiveCredentialResult acquired,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(request.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
            return null;

        var normalizedUrl = environmentUrl.ToString().TrimEnd('/');
        var connections = await _connectionStore.ListAsync(ct).ConfigureAwait(false);

        return connections
            .Where(c => c.Provider == request.Provider)
            .Where(c => string.Equals(c.EnvironmentUrl?.TrimEnd('/'), normalizedUrl, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Cloud == cloud)
            .Where(c =>
                string.IsNullOrWhiteSpace(c.TenantId)
                || string.Equals(c.TenantId, request.TenantId ?? acquired.TenantId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async Task<Profile?> FindExistingProfileAsync(
        string connectionId,
        string credentialId,
        CancellationToken ct)
    {
        var profiles = await _profiles.ListAsync(ct).ConfigureAwait(false);

        return profiles
            .Where(p => string.Equals(p.ConnectionRef, connectionId, StringComparison.OrdinalIgnoreCase))
            .Where(p => string.Equals(p.CredentialRef, credentialId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
