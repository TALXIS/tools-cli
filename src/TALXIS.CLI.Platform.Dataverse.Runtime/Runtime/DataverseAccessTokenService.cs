using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Runtime.Scopes;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Default <see cref="IDataverseAccessTokenService"/>. Drives MSAL via the
/// shared <see cref="MsalClientFactory"/> + token-cache binder so
/// all credential kinds go through one code path for authority selection,
/// scope construction, and cache attachment.
/// </summary>
/// <remarks>
/// An <see cref="InMemoryTokenCache"/> sits in front of MSAL to avoid
/// repeated disk I/O and network calls. Tokens are returned from the
/// in-memory cache when they are still valid beyond a 2-minute proactive
/// renewal buffer (PAC CLI uses 1 minute). Per-key <see cref="SemaphoreSlim"/>
/// prevents stampedes when multiple threads request the same token.
/// </remarks>
public sealed class DataverseAccessTokenService : IDataverseAccessTokenService
{
    private readonly MsalClientFactory _clientFactory;
    private readonly MsalTokenCacheBinder _cacheBinder;
    private readonly ICredentialVault _vault;
    private readonly IEnvironmentReader _env;
    private readonly IHeadlessDetector _headless;
    private readonly ILogger<DataverseAccessTokenService> _logger;
    private readonly InMemoryTokenCache _tokenCache = new();

    public DataverseAccessTokenService(
        MsalClientFactory clientFactory,
        MsalTokenCacheBinder cacheBinder,
        ICredentialVault vault,
        IEnvironmentReader env,
        IHeadlessDetector headless,
        ILogger<DataverseAccessTokenService>? logger = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _cacheBinder = cacheBinder ?? throw new ArgumentNullException(nameof(cacheBinder));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _headless = headless ?? throw new ArgumentNullException(nameof(headless));
        _logger = logger ?? NullLogger<DataverseAccessTokenService>.Instance;
    }

    public async Task<string> AcquireAsync(TALXIS.CLI.Core.Model.Connection connection, Credential credential, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' is missing EnvironmentUrl.");
        if (!Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' EnvironmentUrl '{connection.EnvironmentUrl}' is not a valid absolute URI.");

        return await AcquireForResourceAsync(connection, credential, envUri, ct).ConfigureAwait(false);
    }

    public async Task<string> AcquireForResourceAsync(
        TALXIS.CLI.Core.Model.Connection connection,
        Credential credential,
        Uri resourceUri,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(resourceUri);
        if (!resourceUri.IsAbsoluteUri)
            throw new ArgumentException($"Resource URI '{resourceUri}' must be absolute.", nameof(resourceUri));

        // Use credential-stored scopes when available; otherwise derive from
        // the resource URI dynamically (backward-compatible default).
        var scope = credential.Scopes is { Length: > 0 }
            ? credential.Scopes[0]
            : DataverseScope.BuildDefault(resourceUri);
        var cacheKey = InMemoryTokenCache.BuildKey(credential.Id, credential.TenantId, resourceUri.GetLeftPart(UriPartial.Authority));

        // Fast path: return a cached token that is still valid beyond the
        // proactive renewal buffer.
        var cached = _tokenCache.TryGet(cacheKey);
        if (cached is not null)
            return cached;

        // Slow path: serialize per-key to prevent multiple threads from hitting
        // MSAL for the same token simultaneously.
        var keyLock = _tokenCache.GetKeyLock(cacheKey);
        await keyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock — another thread may have
            // refreshed the token while we were waiting.
            cached = _tokenCache.TryGet(cacheKey);
            if (cached is not null)
                return cached;

            return credential.Kind switch
            {
                CredentialKind.InteractiveBrowser => await AcquirePublicClientSilentAsync(connection, credential, scope, cacheKey, ct).ConfigureAwait(false),
                CredentialKind.ClientSecret => await AcquireClientSecretAsync(connection, credential, scope, cacheKey, ct).ConfigureAwait(false),
                CredentialKind.ClientCertificate => await AcquireClientCertificateAsync(connection, credential, scope, cacheKey, ct).ConfigureAwait(false),
                CredentialKind.WorkloadIdentityFederation => await AcquireFederatedAsync(connection, credential, scope, cacheKey, ct).ConfigureAwait(false),
                CredentialKind.DeviceCode or CredentialKind.ManagedIdentity or CredentialKind.AzureCli or CredentialKind.Pat =>
                    throw new NotSupportedException(
                        $"Credential kind {credential.Kind} is reserved but not yet wired for Dataverse token acquisition in this release. " +
                        "Use InteractiveBrowser, ClientSecret, ClientCertificate, or WorkloadIdentityFederation."),
                _ => throw new NotSupportedException($"Unknown credential kind: {credential.Kind}"),
            };
        }
        finally
        {
            keyLock.Release();
        }
    }

    private async Task<string> AcquirePublicClientSilentAsync(
        TALXIS.CLI.Core.Model.Connection connection, Credential credential, string scope, string cacheKey, CancellationToken ct)
    {
        var app = _clientFactory.BuildPublicClient(connection);
        _cacheBinder.Attach(app.UserTokenCache);

        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(credential.InteractiveAccountId)
                && string.Equals(a.HomeAccountId?.Identifier, credential.InteractiveAccountId, StringComparison.OrdinalIgnoreCase))
            ?? accounts.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(credential.InteractiveUpn)
                && string.Equals(a.Username, credential.InteractiveUpn, StringComparison.OrdinalIgnoreCase))
            // Backward compatibility for credentials created before txc stored
            // a stable interactive account id.
            ?? accounts.FirstOrDefault(a =>
                string.Equals(a.Username, credential.Id, StringComparison.OrdinalIgnoreCase))
            ?? accounts.FirstOrDefault();

        if (account is null)
        {
            throw new InvalidOperationException(
                $"No cached sign-in found for credential '{credential.Id}'. " +
                "Run 'txc config auth login' and retry.");
        }

        try
        {
            var result = await app
                .AcquireTokenSilent(new[] { scope }, account)
                .ExecuteAsync(ct)
                .ConfigureAwait(false);
            _tokenCache.Set(cacheKey, result);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            // In interactive sessions, attempt automatic re-authentication
            // using AcquireTokenInteractive with the actual Dataverse scope
            // that failed — not just identity scopes. This ensures consent
            // is obtained for the correct resource.
            if (!_headless.IsHeadless)
            {
                _logger.LogWarning(
                    "Token for credential '{CredentialId}' expired or requires consent — launching interactive re-authentication with Dataverse scope.",
                    credential.Id);

                try
                {
                    var interactiveResult = await app
                        .AcquireTokenInteractive(new[] { scope })
                        .WithAccount(account)
                        .WithUseEmbeddedWebView(false)
                        .ExecuteAsync(ct)
                        .ConfigureAwait(false);
                    _tokenCache.Set(cacheKey, interactiveResult);
                    return interactiveResult.AccessToken;
                }
                catch (Exception reAuthEx)
                {
                    _logger.LogDebug(reAuthEx, "Automatic re-authentication failed; falling through to original error.");
                }
            }

            throw new InvalidOperationException(
                $"Cached token for '{credential.Id}' expired or is missing consent. " +
                "Run 'txc config auth login' and retry.", ex);
        }
    }

    private async Task<string> AcquireClientSecretAsync(
        TALXIS.CLI.Core.Model.Connection connection, Credential credential, string scope, string cacheKey, CancellationToken ct)
    {
        if (credential.SecretRef is null)
            throw new InvalidOperationException($"Credential '{credential.Id}' (ClientSecret) has no SecretRef.");

        var secret = await _vault.GetSecretAsync(credential.SecretRef, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException(
                $"Credential '{credential.Id}' (ClientSecret) is missing its secret in the vault. " +
                $"Re-run 'txc config auth add-service-principal' to repopulate.");

        var material = new ConfidentialClientMaterial { ClientSecret = secret };
        var app = _clientFactory.BuildConfidentialClient(connection, credential, material);
        _cacheBinder.AttachAppCache(app.AppTokenCache);
        var result = await app
            .AcquireTokenForClient(new[] { scope })
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        _tokenCache.Set(cacheKey, result);
        return result.AccessToken;
    }

    private async Task<string> AcquireClientCertificateAsync(
        TALXIS.CLI.Core.Model.Connection connection, Credential credential, string scope, string cacheKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credential.CertificatePath))
            throw new InvalidOperationException($"Credential '{credential.Id}' (ClientCertificate) has no CertificatePath.");
        if (!File.Exists(credential.CertificatePath))
            throw new InvalidOperationException($"Credential '{credential.Id}' certificate file not found: {credential.CertificatePath}");

        string? password = null;
        if (credential.SecretRef is not null)
            password = await _vault.GetSecretAsync(credential.SecretRef, ct).ConfigureAwait(false);

        var cert = string.IsNullOrEmpty(password)
            ? X509CertificateLoader.LoadPkcs12FromFile(credential.CertificatePath, null)
            : X509CertificateLoader.LoadPkcs12FromFile(credential.CertificatePath, password);

        try
        {
            var material = new ConfidentialClientMaterial { Certificate = cert };
            var app = _clientFactory.BuildConfidentialClient(connection, credential, material);
            _cacheBinder.AttachAppCache(app.AppTokenCache);
            var result = await app
                .AcquireTokenForClient(new[] { scope })
                .ExecuteAsync(ct)
                .ConfigureAwait(false);
            _tokenCache.Set(cacheKey, result);
            return result.AccessToken;
        }
        finally
        {
            cert.Dispose();
        }
    }

    private async Task<string> AcquireFederatedAsync(
        TALXIS.CLI.Core.Model.Connection connection, Credential credential, string scope, string cacheKey, CancellationToken ct)
    {
        var callback = FederatedAssertionCallbacks.AutoSelect(_env, logger: _logger);
        var material = new ConfidentialClientMaterial { AssertionCallback = callback };
        var app = _clientFactory.BuildConfidentialClient(connection, credential, material);
        _cacheBinder.AttachAppCache(app.AppTokenCache);
        var result = await app
            .AcquireTokenForClient(new[] { scope })
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        _tokenCache.Set(cacheKey, result);
        return result.AccessToken;
    }
}
