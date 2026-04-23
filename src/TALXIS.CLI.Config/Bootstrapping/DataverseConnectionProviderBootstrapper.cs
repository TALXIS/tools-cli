using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;

namespace TALXIS.CLI.Config.Bootstrapping;

/// <summary>
/// Dataverse implementation of <see cref="IConnectionProviderBootstrapper"/>.
/// Drives: headless guard → interactive browser login → credential alias
/// resolve + upsert → connection upsert. Called by <c>profile create --url</c>.
/// </summary>
public sealed class DataverseConnectionProviderBootstrapper : IConnectionProviderBootstrapper
{
    private readonly IInteractiveLoginService _login;
    private readonly ICredentialStore _credentials;
    private readonly ConnectionUpsertService _connections;
    private readonly IHeadlessDetector _headless;
    private readonly ILogger<DataverseConnectionProviderBootstrapper> _logger;

    public DataverseConnectionProviderBootstrapper(
        IInteractiveLoginService login,
        ICredentialStore credentials,
        ConnectionUpsertService connections,
        IHeadlessDetector headless,
        ILogger<DataverseConnectionProviderBootstrapper> logger)
    {
        _login = login ?? throw new ArgumentNullException(nameof(login));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _headless = headless ?? throw new ArgumentNullException(nameof(headless));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ProviderKind Provider => ProviderKind.Dataverse;

    public async Task<ProfileBootstrapResult> BootstrapAsync(
        ProfileBootstrapRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        // Interactive browser is forbidden in CI. Throws HeadlessAuthRequiredException
        // with the permitted-kinds message — same behaviour as `auth login`.
        _headless.EnsureKindAllowed(CredentialKind.InteractiveBrowser);

        _logger.LogInformation("Starting interactive sign-in for '{Url}'...", request.EnvironmentUrl);
        var login = await _login.LoginAsync(request.TenantId, request.Cloud, ct).ConfigureAwait(false);

        var alias = await CredentialAliasResolver
            .ResolveForUpnAsync(_credentials, login.Upn, ct)
            .ConfigureAwait(false);

        var credential = new Credential
        {
            Id = alias,
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = login.TenantId,
            Cloud = request.Cloud,
            Description = $"Interactive sign-in ({login.Upn})",
        };
        await _credentials.UpsertAsync(credential, ct).ConfigureAwait(false);

        var upsert = await _connections.ValidateAndUpsertAsync(
            request.Name,
            request.Provider,
            request.EnvironmentUrl,
            request.Cloud,
            organizationId: null,
            tenantId: request.TenantId ?? login.TenantId,
            description: request.Description,
            ct).ConfigureAwait(false);

        if (upsert.Error is not null)
            return new ProfileBootstrapResult(credential, null, login.Upn, upsert.Error);

        return new ProfileBootstrapResult(credential, upsert.Connection, login.Upn, null);
    }
}
