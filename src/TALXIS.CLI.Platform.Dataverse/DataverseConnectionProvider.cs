using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Platform.Dataverse.Msal;

namespace TALXIS.CLI.Platform.Dataverse;

/// <summary>
/// <see cref="IConnectionProvider"/> for <see cref="ProviderKind.Dataverse"/>.
/// Validates that the connection metadata is well-formed and that the
/// credential kind is supported by this provider. Token acquisition and
/// WhoAmI checks land in the refactor milestone once auth commands are
/// wired — this v1 provider reports structural validation only so the
/// profile/connection command plumbing can round-trip safely.
/// </summary>
public sealed class DataverseConnectionProvider : IConnectionProvider
{
    private static readonly FrozenSet<CredentialKind> Supported = new[]
    {
        CredentialKind.InteractiveBrowser,
        CredentialKind.DeviceCode,
        CredentialKind.ClientSecret,
        CredentialKind.ClientCertificate,
        CredentialKind.WorkloadIdentityFederation,
        CredentialKind.ManagedIdentity,
        CredentialKind.AzureCli,
    }.ToFrozenSet();

    private readonly DataverseMsalClientFactory _clientFactory;
    private readonly IDataverseLiveChecker _liveChecker;
    private readonly ILogger<DataverseConnectionProvider> _logger;

    public DataverseConnectionProvider(
        DataverseMsalClientFactory clientFactory,
        IDataverseLiveChecker liveChecker,
        ILogger<DataverseConnectionProvider>? logger = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _liveChecker = liveChecker ?? throw new ArgumentNullException(nameof(liveChecker));
        _logger = logger ?? NullLogger<DataverseConnectionProvider>.Instance;
    }

    public ProviderKind ProviderKind => ProviderKind.Dataverse;

    public IReadOnlySet<CredentialKind> SupportedCredentialKinds => Supported;

    public async Task ValidateAsync(Connection connection, Credential credential, ValidationMode mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        if (connection.Provider != ProviderKind.Dataverse)
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' has provider {connection.Provider}, expected {ProviderKind.Dataverse}.");

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
            throw new InvalidOperationException(
                $"Dataverse connection '{connection.Id}' is missing EnvironmentUrl.");

        if (!Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var envUri) ||
            (envUri.Scheme != Uri.UriSchemeHttp && envUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Dataverse connection '{connection.Id}' EnvironmentUrl '{connection.EnvironmentUrl}' is not an absolute http(s) URI.");
        }

        if (!Supported.Contains(credential.Kind))
        {
            throw new InvalidOperationException(
                $"Credential kind {credential.Kind} is not supported by the Dataverse provider.");
        }

        // Probe MSAL builder wiring so authority resolution errors surface during `config profile validate`
        // rather than at first token acquisition.
        _ = DataverseMsalClientFactory.ResolveAuthority(connection, credential);

        _logger.LogDebug(
            "Dataverse connection '{ConnectionId}' validated structurally (envUrl={EnvUrl}, cloud={Cloud}, kind={Kind}).",
            connection.Id, envUri, connection.Cloud, credential.Kind);

        if (mode == ValidationMode.Live)
        {
            var result = await _liveChecker.CheckAsync(connection, credential, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Dataverse WhoAmI succeeded (userId={UserId}, orgId={OrgId}).",
                result.UserId, result.OrganizationId);
        }
    }
}
