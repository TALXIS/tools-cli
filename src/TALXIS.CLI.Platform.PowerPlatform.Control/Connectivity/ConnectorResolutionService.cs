using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>Exposes connector resolution to features that live in other layers.</summary>
public sealed class ConnectorResolutionService : IConnectorResolver
{
    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectorResolver _resolver;

    public ConnectorResolutionService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        IDataverseQueryService query,
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var logger = loggerFactory?.CreateLogger<ConnectorResolutionService>() ?? NullLogger<ConnectorResolutionService>.Instance;

        _targets = new ConnectivityTargetResolver(
            resolver ?? throw new ArgumentNullException(nameof(resolver)),
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            logger);
        _resolver = new ConnectorResolver(
            new ConnectivityApiClient(tokens, httpFactory ?? DefaultHttpClientFactoryWrapper.Instance, logger),
            query ?? throw new ArgumentNullException(nameof(query)),
            logger);
    }

    public async Task<ResolvedConnectorInfo> ResolveAsync(string? profileName, string connector, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _resolver.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        return new ResolvedConnectorInfo(
            resolved.ApiId, resolved.LogicalName, resolved.DisplayName, resolved.IsCustom, resolved.XrmConnectorId);
    }
}
