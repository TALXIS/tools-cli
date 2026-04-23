using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Shared helper that every refactored Dataverse leaf command uses to turn
/// a <c>--profile</c> string (or null => resolver defaults / TXC_PROFILE)
/// into a ready-to-use <see cref="DataverseConnection"/>.
/// </summary>
public static class DataverseCommandBridge
{
    public static async Task<DataverseConnection> ConnectAsync(string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var factory = TxcServices.Get<IDataverseConnectionFactory>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        return await factory.ConnectAsync(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the active profile + primes the MSAL cache by acquiring a
    /// token once. Used by parent commands before spawning a subprocess so
    /// that auth failures (e.g. the "Run <c>txc config auth login</c>"
    /// message wrapping <see cref="Microsoft.Identity.Client.MsalUiRequiredException"/>)
    /// surface in the parent process — which has a TTY and the main log
    /// sinks — rather than inside the child.
    /// </summary>
    /// <remarks>
    /// Success does not guarantee the child will succeed (refresh tokens can
    /// expire between calls), but in practice the gap is milliseconds.
    /// </remarks>
    public static async Task<PrimedProfile> PrimeTokenAsync(string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var svc = TxcServices.Get<IDataverseAccessTokenService>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);

        ValidateDataverseProfile(context.Connection);
        var envUri = ParseEnvironmentUrl(context.Connection);

        _ = await svc.AcquireForResourceAsync(context.Connection, context.Credential, envUri, ct).ConfigureAwait(false);

        return new PrimedProfile(context.Connection, context.Credential, envUri);
    }

    /// <summary>
    /// Builds a per-call token provider suitable for
    /// <c>Microsoft.PowerPlatform.Dataverse.Client.ServiceClient</c>'s
    /// <c>Func&lt;string, Task&lt;string&gt;&gt;</c> constructor.
    /// </summary>
    /// <remarks>
    /// The provider honours the resource URI the SDK asks for — which may
    /// be a canonicalized/redirected org URL rather than the
    /// <c>Connection.EnvironmentUrl</c> we resolved from the profile. See
    /// <see cref="IDataverseAccessTokenService.AcquireForResourceAsync"/>.
    /// Cancellation of the returned provider follows
    /// <paramref name="ct"/>; callers that need different per-call
    /// cancellation should wrap the returned delegate themselves.
    /// </remarks>
    public static async Task<(Uri EnvironmentUrl, Func<string, Task<string>> TokenProvider)> BuildTokenProviderAsync(
        string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var svc = TxcServices.Get<IDataverseAccessTokenService>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);

        ValidateDataverseProfile(context.Connection);
        var envUri = ParseEnvironmentUrl(context.Connection);

        var connection = context.Connection;
        var credential = context.Credential;

        Func<string, Task<string>> provider = async (resourceUriString) =>
        {
            if (string.IsNullOrWhiteSpace(resourceUriString))
                throw new ArgumentException("Dataverse token provider received an empty resource URI.", nameof(resourceUriString));
            if (!Uri.TryCreate(resourceUriString, UriKind.Absolute, out var resourceUri))
                throw new ArgumentException($"Dataverse token provider received an invalid resource URI: '{resourceUriString}'.", nameof(resourceUriString));
            return await svc.AcquireForResourceAsync(connection, credential, resourceUri, ct).ConfigureAwait(false);
        };

        return (envUri, provider);
    }

    /// <summary>
    /// Transitional helper for callers that still need a Dataverse
    /// connection string (tests, and any legacy code path not yet migrated
    /// to the token-provider callback).
    /// </summary>
    /// <remarks>
    /// Supports only <see cref="CredentialKind.ClientSecret"/>. Every other
    /// kind — including <see cref="CredentialKind.InteractiveBrowser"/> —
    /// must use <see cref="BuildTokenProviderAsync"/> + the
    /// <c>ServiceClient(Uri, Func&lt;string, Task&lt;string&gt;&gt;, ...)</c>
    /// constructor instead; attempting to inline an interactive session
    /// into a connection string is by design impossible.
    /// </remarks>
    public static async Task<string> BuildConnectionStringAsync(string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        ValidateDataverseProfile(context.Connection);

        var url = context.Connection.EnvironmentUrl!.TrimEnd('/');
        var cred = context.Credential;

        if (cred.Kind != CredentialKind.ClientSecret)
            throw new NotSupportedException(
                $"Credential kind '{cred.Kind}' cannot be serialized into a Dataverse connection string. " +
                "Use a token-provider path (BuildTokenProviderAsync) instead.");

        if (string.IsNullOrWhiteSpace(cred.ApplicationId))
            throw new InvalidOperationException($"Credential '{cred.Id}' is missing ApplicationId.");
        if (cred.SecretRef is null)
            throw new InvalidOperationException($"Credential '{cred.Id}' has no SecretRef for its client secret.");
        var vault = TxcServices.Get<ICredentialVault>();
        var secret = await vault.GetSecretAsync(cred.SecretRef, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Vault could not return a client secret for credential '{cred.Id}'.");
        return $"AuthType=ClientSecret;Url={url};ClientId={cred.ApplicationId};ClientSecret={secret}";
    }

    private static void ValidateDataverseProfile(TALXIS.CLI.Core.Model.Connection connection)
    {
        if (connection.Provider != ProviderKind.Dataverse)
            throw new InvalidOperationException(
                $"Connection '{connection.Id}' has provider {connection.Provider}, expected {ProviderKind.Dataverse}.");
        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' is missing EnvironmentUrl.");
    }

    private static Uri ParseEnvironmentUrl(TALXIS.CLI.Core.Model.Connection connection)
    {
        if (!Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException(
                $"Dataverse connection '{connection.Id}' EnvironmentUrl '{connection.EnvironmentUrl}' is not a valid absolute URI.");
        return envUri;
    }

    /// <summary>Result of <see cref="PrimeTokenAsync"/>: the resolved profile pair + validated environment URL.</summary>
    public sealed record PrimedProfile(TALXIS.CLI.Core.Model.Connection Connection, Credential Credential, Uri EnvironmentUrl);
}
