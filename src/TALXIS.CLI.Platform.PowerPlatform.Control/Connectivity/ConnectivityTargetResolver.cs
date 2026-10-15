using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>The profile resolved to everything a connectivity call needs.</summary>
internal sealed record ConnectivityTarget(
    string? ProfileName,
    Connection Connection,
    Credential Credential,
    Guid EnvironmentId,
    Uri BaseUri);

/// <summary>
/// Resolves a profile to its connection, credential and per-environment API
/// host. The stored environment id wins; otherwise it is looked up in the
/// platform catalog from the Dataverse URL.
/// </summary>
internal sealed class ConnectivityTargetResolver
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly ILogger _logger;

    public ConnectivityTargetResolver(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        ILogger logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConnectivityTarget> ResolveAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environmentId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);
        var baseUri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(
            environmentId, ctx.Connection.Cloud ?? CloudInstance.Public);

        return new ConnectivityTarget(profileName, ctx.Connection, ctx.Credential, environmentId, baseUri);
    }

    private async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext ctx, CancellationToken ct)
    {
        if (ctx.Connection.EnvironmentId.HasValue)
            return ctx.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(ctx.Connection.EnvironmentUrl)
            || !Uri.TryCreate(ctx.Connection.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' has no EnvironmentId and no usable EnvironmentUrl to resolve one from.");
        }

        _logger.LogDebug(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via the Power Platform catalog...",
            ctx.Connection.Id);

        var environment = await _catalog
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, environmentUrl, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve a Power Platform environment for '{environmentUrl}'. " +
                "Set the environment ID on the connection, or re-run the connection live check.");

        return environment.EnvironmentId;
    }
}
