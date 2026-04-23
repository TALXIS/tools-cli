using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Bootstrapping;

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

        _logger.LogInformation("Starting interactive sign-in for '{Url}'...", request.EnvironmentUrl);
        var acquired = await InteractiveCredentialBootstrapper.AcquireAndPersistAsync(
            _login, _credentials, _headless,
            request.TenantId, request.Cloud, explicitAlias: null, ct).ConfigureAwait(false);

        var upsert = await _connections.ValidateAndUpsertAsync(
            request.Name,
            request.Provider,
            request.EnvironmentUrl,
            request.Cloud,
            organizationId: null,
            tenantId: request.TenantId ?? acquired.TenantId,
            description: request.Description,
            ct).ConfigureAwait(false);

        if (upsert.Error is not null)
            return new ProfileBootstrapResult(acquired.Credential, null, acquired.Upn, upsert.Error);

        return new ProfileBootstrapResult(acquired.Credential, upsert.Connection, acquired.Upn, null);
    }
}
