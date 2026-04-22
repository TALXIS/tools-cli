using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Disposable wrapper around a <see cref="ServiceClient"/> plus its optional
/// <see cref="DataverseAuthTokenProvider"/>. Disposing releases both.
/// </summary>
public sealed class DataverseConnection : IDisposable
{
    public ServiceClient Client { get; }
    public DataverseAuthTokenProvider? TokenProvider { get; }

    internal DataverseConnection(ServiceClient client, DataverseAuthTokenProvider? tokenProvider)
    {
        Client = client;
        TokenProvider = tokenProvider;
    }

    public void Dispose()
    {
        Client.Dispose();
        TokenProvider?.Dispose();
    }
}

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

        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out Uri? instanceUri))
        {
            throw new InvalidOperationException(
                $"Invalid Dataverse environment URL '{environmentUrl}'. Pass a valid absolute URL to --environment or set DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
        }

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

    /// <summary>
    /// One-call resolve-and-connect. Handles env-var fallbacks, validates that auth
    /// is configured, and returns a disposable wrapper owning both the
    /// <see cref="ServiceClient"/> and optional token provider.
    /// Throws <see cref="InvalidOperationException"/> when neither a connection
    /// string nor an environment URL can be resolved.
    /// </summary>
    public static DataverseConnection Connect(
        string? connectionStringOption,
        string? environmentUrlOption,
        bool deviceCode,
        bool verbose,
        ILogger? logger)
    {
        string? resolvedConnectionString = ResolveConnectionString(connectionStringOption);
        string? resolvedEnvironmentUrl = ResolveEnvironmentUrl(environmentUrlOption);

        if (string.IsNullOrWhiteSpace(resolvedConnectionString) && string.IsNullOrWhiteSpace(resolvedEnvironmentUrl))
        {
            throw new InvalidOperationException(
                "Dataverse authentication is required. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
        }

        var client = Create(resolvedConnectionString, resolvedEnvironmentUrl, deviceCode, verbose, logger, out var tokenProvider);
        return new DataverseConnection(client, tokenProvider);
    }
}
