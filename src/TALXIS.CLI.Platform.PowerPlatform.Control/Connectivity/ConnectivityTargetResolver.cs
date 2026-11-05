using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>The profile resolved to everything a connectivity call needs.</summary>
internal sealed record ConnectivityTarget(
    string? ProfileName,
    Connection Connection,
    Credential Credential,
    Guid EnvironmentId,
    Uri BaseUri);

/// <summary>Resolves a profile to its connection, credential and per-environment API host.</summary>
internal sealed class ConnectivityTargetResolver
{
    private readonly IConfigurationResolver _resolver;
    private readonly IEnvironmentIdResolver _environmentIds;

    public ConnectivityTargetResolver(IConfigurationResolver resolver, IEnvironmentIdResolver environmentIds)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _environmentIds = environmentIds ?? throw new ArgumentNullException(nameof(environmentIds));
    }

    public async Task<ConnectivityTarget> ResolveAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environmentId = await _environmentIds.ResolveAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);
        var baseUri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(
            environmentId, ctx.Connection.Cloud ?? CloudInstance.Public);

        return new ConnectivityTarget(profileName, ctx.Connection, ctx.Credential, environmentId, baseUri);
    }
}
