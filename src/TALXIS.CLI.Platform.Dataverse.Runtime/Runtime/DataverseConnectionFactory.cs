using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Default <see cref="IDataverseConnectionFactory"/>. Delegates token
/// acquisition to <see cref="IDataverseAccessTokenService"/> and wires the
/// resulting bearer into a <see cref="ServiceClient"/> via its
/// token-provider callback. No connection strings, no env-var fallbacks:
/// everything flows from the resolved profile.
/// </summary>
public sealed class DataverseConnectionFactory : IDataverseConnectionFactory
{
    private readonly IDataverseAccessTokenService _tokens;
    private readonly ILogger<DataverseConnectionFactory> _logger;

    public DataverseConnectionFactory(
        IDataverseAccessTokenService tokens,
        ILogger<DataverseConnectionFactory>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _logger = logger ?? NullLogger<DataverseConnectionFactory>.Instance;
    }

    public Task<DataverseConnection> ConnectAsync(ResolvedProfileContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Connection.Provider != ProviderKind.Dataverse)
            throw new InvalidOperationException(
                $"Connection '{context.Connection.Id}' has provider {context.Connection.Provider}, expected {ProviderKind.Dataverse}.");
        if (string.IsNullOrWhiteSpace(context.Connection.EnvironmentUrl))
            throw new InvalidOperationException(
                $"Dataverse connection '{context.Connection.Id}' is missing EnvironmentUrl.");
        if (!Uri.TryCreate(context.Connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException(
                $"Dataverse connection '{context.Connection.Id}' EnvironmentUrl '{context.Connection.EnvironmentUrl}' is not a valid absolute URI.");

        _logger.LogDebug(
            "Connecting to Dataverse env '{EnvUrl}' via profile '{Profile}' (credential kind={Kind}, source={Source}).",
            envUri, context.Profile?.Id ?? "(ephemeral)", context.Credential.Kind, context.Source);

        // ServiceClient invokes the token provider on every request that needs a bearer;
        // the service caches internally based on freshness, and the underlying
        // IDataverseAccessTokenService delegates to MSAL which provides its own caching.
        var conn = context.Connection;
        var cred = context.Credential;
        var client = new ServiceClient(
            envUri,
            async resource =>
            {
                // Honor the resource requested by the Dataverse SDK because it may
                // canonicalize or redirect the organization URL before asking for a token.
                if (!string.IsNullOrWhiteSpace(resource) &&
                    Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri))
                {
                    return await _tokens.AcquireForResourceAsync(conn, cred, resourceUri, ct).ConfigureAwait(false);
                }

                return await _tokens.AcquireAsync(conn, cred, ct).ConfigureAwait(false);
            },
            useUniqueInstance: true,
            logger: null);

        // Prevent indefinite hangs when token acquisition fails silently.
        ServiceClient.MaxConnectionTimeout = TimeSpan.FromMinutes(2);

        if (!client.IsReady)
        {
            var error = client.LastError;
            client.Dispose();
            throw new InvalidOperationException(
                $"Failed to establish Dataverse connection to '{envUri}' for profile '{context.Profile?.Id ?? "(ephemeral)"}': {error}");
        }

        // Capture the token provider so DataverseConnection can create HttpClients
        // for direct Web API calls (needed for metadata operations like RequiredLevel
        // that the SDK's UpdateAttributeRequest doesn't handle correctly).
        Func<string, Task<string>> tokenProvider = async resource =>
        {
            if (!string.IsNullOrWhiteSpace(resource) &&
                Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri))
            {
                return await _tokens.AcquireForResourceAsync(conn, cred, resourceUri, ct).ConfigureAwait(false);
            }
            return await _tokens.AcquireAsync(conn, cred, ct).ConfigureAwait(false);
        };

        return Task.FromResult(DataverseConnection.FromServiceClient(client, tokenProvider));
    }
}
