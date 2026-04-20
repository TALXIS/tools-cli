using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Centralised construction of <see cref="ServiceClient"/> instances from either an
/// explicit connection string or an environment URL combined with the PAC-style MSAL
/// auth flow surfaced by <see cref="DataverseAuthTokenProvider"/>. Honours the
/// env-var fallbacks used by the package deploy command.
/// </summary>
public static class ServiceClientFactory
{
    public const string ConnectionStringEnvVar = "DATAVERSE_CONNECTION_STRING";
    public const string ConnectionStringTxcEnvVar = "TXC_DATAVERSE_CONNECTION_STRING";
    public const string EnvironmentUrlEnvVar = "DATAVERSE_ENVIRONMENT_URL";
    public const string EnvironmentUrlTxcEnvVar = "TXC_DATAVERSE_ENVIRONMENT_URL";

    public static string? ResolveConnectionString(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return System.Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? System.Environment.GetEnvironmentVariable(ConnectionStringTxcEnvVar);
    }

    public static string? ResolveEnvironmentUrl(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return System.Environment.GetEnvironmentVariable(EnvironmentUrlEnvVar)
            ?? System.Environment.GetEnvironmentVariable(EnvironmentUrlTxcEnvVar);
    }

    /// <summary>
    /// Builds a <see cref="ServiceClient"/> from the resolved connection string, or — when only
    /// an environment URL is known — via the MSAL-based token provider.
    /// </summary>
    /// <param name="connectionString">Optional connection string (already resolved from options/env).</param>
    /// <param name="environmentUrl">Optional environment URL (already resolved from options/env).</param>
    /// <param name="deviceCode">Use device-code flow instead of interactive browser.</param>
    /// <param name="verbose">Emit verbose auth traces.</param>
    /// <param name="logger">Optional logger forwarded to the SDK.</param>
    /// <param name="tokenProvider">
    /// Out parameter carrying the underlying <see cref="DataverseAuthTokenProvider"/>
    /// when auth is performed via environment URL. Caller owns disposal.
    /// </param>
    public static ServiceClient Create(
        string? connectionString,
        string? environmentUrl,
        bool deviceCode,
        bool verbose,
        ILogger? logger,
        out DataverseAuthTokenProvider? tokenProvider)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            tokenProvider = null;
            var client = new ServiceClient(connectionString, logger);
            ThrowIfNotReady(client);
            return client;
        }

        if (string.IsNullOrWhiteSpace(environmentUrl))
        {
            throw new InvalidOperationException(
                "Dataverse authentication requires either --connection-string or --environment (or the DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL env vars).");
        }

        Uri instanceUri = new(environmentUrl);
        var provider = new DataverseAuthTokenProvider(instanceUri, deviceCode, verbose);
        tokenProvider = provider;

        try
        {
            // ServiceClient token provider delegate receives the target resource URL and must
            // return a bearer token. Delegate to the MSAL-backed provider which handles
            // silent → device-code/interactive fallback and disk caching.
            var client = new ServiceClient(
                instanceUri,
                resourceUrl => provider.GetAccessTokenAsync(new Uri(resourceUrl)),
                useUniqueInstance: true,
                logger);

            ThrowIfNotReady(client);
            return client;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private static void ThrowIfNotReady(ServiceClient client)
    {
        if (!client.IsReady)
        {
            string message = client.LastError;
            client.Dispose();
            throw new InvalidOperationException(
                $"Failed to establish Dataverse connection: {message}");
        }
    }
}
