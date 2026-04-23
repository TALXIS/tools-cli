using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Platform.Dataverse.Msal;
using TALXIS.CLI.Platform.Dataverse.Scopes;
using TALXIS.CLI.Config.Resolution;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Default <see cref="IDataverseAccessTokenService"/>. Drives MSAL via the
/// shared <see cref="DataverseMsalClientFactory"/> + token-cache binder so
/// all credential kinds go through one code path for authority selection,
/// scope construction, and cache attachment.
/// </summary>
public sealed class DataverseAccessTokenService : IDataverseAccessTokenService
{
    private readonly DataverseMsalClientFactory _clientFactory;
    private readonly DataverseTokenCacheBinder _cacheBinder;
    private readonly ICredentialVault _vault;
    private readonly IEnvironmentReader _env;
    private readonly ILogger<DataverseAccessTokenService> _logger;

    public DataverseAccessTokenService(
        DataverseMsalClientFactory clientFactory,
        DataverseTokenCacheBinder cacheBinder,
        ICredentialVault vault,
        IEnvironmentReader env,
        ILogger<DataverseAccessTokenService>? logger = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _cacheBinder = cacheBinder ?? throw new ArgumentNullException(nameof(cacheBinder));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger = logger ?? NullLogger<DataverseAccessTokenService>.Instance;
    }

    public async Task<string> AcquireAsync(TALXIS.CLI.Config.Model.Connection connection, Credential credential, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.EnvironmentUrl))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' is missing EnvironmentUrl.");
        if (!Uri.TryCreate(connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
            throw new InvalidOperationException($"Dataverse connection '{connection.Id}' EnvironmentUrl '{connection.EnvironmentUrl}' is not a valid absolute URI.");

        return await AcquireForResourceAsync(connection, credential, envUri, ct).ConfigureAwait(false);
    }

    public async Task<string> AcquireForResourceAsync(
        TALXIS.CLI.Config.Model.Connection connection,
        Credential credential,
        Uri resourceUri,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(resourceUri);
        if (!resourceUri.IsAbsoluteUri)
            throw new ArgumentException($"Resource URI '{resourceUri}' must be absolute.", nameof(resourceUri));

        var scope = DataverseScope.BuildDefault(resourceUri);

        return credential.Kind switch
        {
            CredentialKind.InteractiveBrowser => await AcquirePublicClientSilentAsync(connection, credential, scope, ct).ConfigureAwait(false),
            CredentialKind.ClientSecret => await AcquireClientSecretAsync(connection, credential, scope, ct).ConfigureAwait(false),
            CredentialKind.ClientCertificate => await AcquireClientCertificateAsync(connection, credential, scope, ct).ConfigureAwait(false),
            CredentialKind.WorkloadIdentityFederation => await AcquireFederatedAsync(connection, credential, scope, ct).ConfigureAwait(false),
            CredentialKind.DeviceCode or CredentialKind.ManagedIdentity or CredentialKind.AzureCli or CredentialKind.Pat =>
                throw new NotSupportedException(
                    $"Credential kind {credential.Kind} is reserved but not yet wired for Dataverse token acquisition in this release. " +
                    "Use InteractiveBrowser, ClientSecret, ClientCertificate, or WorkloadIdentityFederation."),
            _ => throw new NotSupportedException($"Unknown credential kind: {credential.Kind}"),
        };
    }

    private async Task<string> AcquirePublicClientSilentAsync(
        TALXIS.CLI.Config.Model.Connection connection, Credential credential, string scope, CancellationToken ct)
    {
        var app = _clientFactory.BuildPublicClient(connection);
        _cacheBinder.Attach(app.UserTokenCache);

        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        // Prefer the account whose UPN matches the credential alias (case-insensitive).
        var account = accounts.FirstOrDefault(a =>
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
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            throw new InvalidOperationException(
                $"Cached token for '{credential.Id}' expired or is missing consent. " +
                "Run 'txc config auth login' and retry.", ex);
        }
    }

    private async Task<string> AcquireClientSecretAsync(
        TALXIS.CLI.Config.Model.Connection connection, Credential credential, string scope, CancellationToken ct)
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
        var result = await app
            .AcquireTokenForClient(new[] { scope })
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        return result.AccessToken;
    }

    private async Task<string> AcquireClientCertificateAsync(
        TALXIS.CLI.Config.Model.Connection connection, Credential credential, string scope, CancellationToken ct)
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
            var result = await app
                .AcquireTokenForClient(new[] { scope })
                .ExecuteAsync(ct)
                .ConfigureAwait(false);
            return result.AccessToken;
        }
        finally
        {
            cert.Dispose();
        }
    }

    private async Task<string> AcquireFederatedAsync(
        TALXIS.CLI.Config.Model.Connection connection, Credential credential, string scope, CancellationToken ct)
    {
        var callback = FederatedAssertionCallbacks.AutoSelect(_env, logger: _logger);
        var material = new ConfidentialClientMaterial { AssertionCallback = callback };
        var app = _clientFactory.BuildConfidentialClient(connection, credential, material);
        var result = await app
            .AcquireTokenForClient(new[] { scope })
            .ExecuteAsync(ct)
            .ConfigureAwait(false);
        return result.AccessToken;
    }
}
