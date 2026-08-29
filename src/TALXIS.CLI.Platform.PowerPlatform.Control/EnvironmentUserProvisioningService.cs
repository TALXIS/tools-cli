using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Implements <see cref="IEnvironmentUserProvisioningService"/> by resolving
/// the target Entra user via Microsoft Graph and provisioning them into the
/// environment via the BAP admin <c>addUser</c> endpoint. Backs
/// <c>txc security user add --environment ...</c>.
/// </summary>
public sealed class EnvironmentUserProvisioningService : IEnvironmentUserProvisioningService
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly MicrosoftGraphClient _graph;
    private readonly BapAdminApiClient _bap;
    private readonly EnvironmentSettingsClient _settings;

    public EnvironmentUserProvisioningService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        MicrosoftGraphClient graph,
        EnvironmentSettingsClient settings,
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _bap = new BapAdminApiClient(tokens ?? throw new ArgumentNullException(nameof(tokens)), httpFactory);
    }

    public async Task<EnvironmentUserProvisionResult> ProvisionUserAsync(
        string? profileName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdOrUpn);

        var ctx = await ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        var resolvedEnvironmentId = await ResolveEnvironmentIdAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);
        var (aadObjectId, upn, displayName) = await ResolveGraphUserAsync(ctx.Connection, ctx.Credential, userIdOrUpn, ct)
            .ConfigureAwait(false);

        await _bap.AddUserToEnvironmentAsync(ctx.Connection, ctx.Credential, resolvedEnvironmentId, aadObjectId, ct)
            .ConfigureAwait(false);

        return new EnvironmentUserProvisionResult(aadObjectId, upn, displayName);
    }

    private async Task<ResolvedProfileContext> ResolveContextAsync(
        string? profileName,
        Guid? environmentId,
        CancellationToken ct)
    {
        var context = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        if (!environmentId.HasValue)
            return context;

        var environment = (await _catalog.ListAsync(context.Connection, context.Credential, ct).ConfigureAwait(false))
            .SingleOrDefault(candidate => candidate.EnvironmentId == environmentId.Value)
            ?? throw new InvalidOperationException(
                $"Power Platform environment '{environmentId}' was not found or is not accessible with the resolved profile.");

        return new ResolvedProfileContext(
            context.Profile,
            new Connection
            {
                Id = context.Connection.Id,
                Provider = context.Connection.Provider,
                Description = context.Connection.Description,
                EnvironmentUrl = environment.EnvironmentUrl.AbsoluteUri,
                OrganizationId = environment.OrganizationId?.ToString(),
                EnvironmentId = environment.EnvironmentId,
                Cloud = context.Connection.Cloud,
                TenantId = context.Connection.TenantId,
                DisplayName = environment.DisplayName,
                EnvironmentType = environment.EnvironmentType,
                CreatedAt = context.Connection.CreatedAt,
                UpdatedAt = context.Connection.UpdatedAt,
                ExtraFields = context.Connection.ExtraFields,
            },
            context.Credential,
            context.Source);
    }

    private async Task<(Guid AadObjectId, string? UserPrincipalName, string? DisplayName)> ResolveGraphUserAsync(
        Connection connection,
        Credential credential,
        string userIdOrUpn,
        CancellationToken ct)
    {
        var trimmed = userIdOrUpn.Trim();
        var filter = Guid.TryParse(trimmed, out var id)
            ? $"id eq '{id}'"
            : $"userPrincipalName eq '{GraphODataFilterSupport.EscapeODataString(trimmed)}'";

        var matches = await _graph.ListUsersAsync(connection, credential, filter, top: 5, ct).ConfigureAwait(false);

        if (matches.Count == 0)
            throw new InvalidOperationException($"Entra user '{userIdOrUpn}' was not found via Microsoft Graph.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple Entra users matched '{userIdOrUpn}'. Use the Entra object ID to disambiguate.");

        var user = matches[0];
        return (user.Id, user.UserPrincipalName, user.DisplayName);
    }

    private async Task<Guid> ResolveEnvironmentIdAsync(Connection connection, Credential credential, CancellationToken ct)
    {
        if (connection.EnvironmentId.HasValue)
            return connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl)
            || !Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' has no EnvironmentUrl or EnvironmentId.");
        }

        var environment = await _catalog
            .TryGetByEnvironmentUrlAsync(connection, credential, environmentUrl, ct)
            .ConfigureAwait(false);

        return environment?.EnvironmentId
            ?? throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{connection.EnvironmentUrl}'.");
    }

    /// <inheritdoc />
    public Task SelfElevateAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        CancellationToken ct)
        => _settings.SelfElevateAsync(connection, credential, environmentId, ct);
}
