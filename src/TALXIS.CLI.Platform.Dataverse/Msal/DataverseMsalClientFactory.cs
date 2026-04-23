using System.Security.Cryptography.X509Certificates;
using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Authority;

namespace TALXIS.CLI.Platform.Dataverse.Msal;

/// <summary>
/// Callback that returns a client assertion (JWT) for federated credential
/// flows (GitHub OIDC, ADO Workload Identity). Registered per-credential
/// when the Credential's <see cref="CredentialKind"/> is
/// <see cref="CredentialKind.WorkloadIdentityFederation"/>.
/// </summary>
public delegate Task<string> ClientAssertionCallback(CancellationToken ct);

/// <summary>
/// Inputs required to rehydrate a confidential-client secret or certificate
/// from <see cref="ICredentialVault"/> at build time.
/// </summary>
public sealed record ConfidentialClientMaterial
{
    public string? ClientSecret { get; init; }
    public X509Certificate2? Certificate { get; init; }
    public ClientAssertionCallback? AssertionCallback { get; init; }

    public bool IsEmpty =>
        string.IsNullOrEmpty(ClientSecret) && Certificate is null && AssertionCallback is null;
}

/// <summary>
/// Builds MSAL public- and confidential-client applications wired to the
/// correct Dataverse authority. Callers (DataverseConnectionProvider,
/// auth commands) never instantiate MSAL builders directly — this is the
/// single place that pins our client constants and authority policy.
/// </summary>
/// <remarks>
/// Pinned choices (parity with pac 2.6.3, see <c>temp/pac-auth-research.md</c>):
/// <list type="bullet">
///   <item>Public client id <c>9cee029c-6210-4654-90bb-17e6e9d36617</c>, redirect <c>http://localhost</c>.</item>
///   <item><c>validateAuthority: false</c> on every builder — required for sovereign clouds.</item>
///   <item>Certificate flow sends <c>sendX5C: true</c> to enable subject-name/issuer auth.</item>
/// </list>
/// Token cache registration is handled separately by the caller so that the
/// same <c>MsalCacheHelper</c> instance is reused across clients (critical for
/// keeping Keychain prompt count low on macOS — see keychain-prompt-research.md).
/// </remarks>
public sealed class DataverseMsalClientFactory
{
    /// <summary>
    /// Public-client application id used by pac CLI. Reused verbatim so that
    /// first-run users don't need to register their own Entra app. Confidential
    /// flows use the Credential's own <c>ApplicationId</c> instead.
    /// </summary>
    public const string PublicClientId = "9cee029c-6210-4654-90bb-17e6e9d36617";

    /// <summary>Redirect URI registered on the <see cref="PublicClientId"/> app.</summary>
    public const string PublicRedirectUri = "http://localhost";

    /// <summary>
    /// Builds a public-client application (interactive / device-code / silent)
    /// for the given connection. Cloud precedence: explicit on
    /// <paramref name="connection"/> → inferred from <c>EnvironmentUrl</c> →
    /// <see cref="CloudInstance.Public"/>.
    /// </summary>
    public IPublicClientApplication BuildPublicClient(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var authority = ResolveAuthority(connection);

        return PublicClientApplicationBuilder
            .Create(PublicClientId)
            .WithRedirectUri(PublicRedirectUri)
            .WithAuthority(authority.AbsoluteUri, validateAuthority: false)
            .Build();
    }

    /// <summary>
    /// Builds a public-client application for <c>config auth login</c>, when
    /// we don't yet have a <see cref="Connection"/>. <paramref name="tenantId"/>
    /// of <c>null</c> resolves to <c>organizations</c> — MSAL then infers the
    /// tenant from whatever account the user picks in the browser. We
    /// deliberately avoid <c>/common</c> because personal MS accounts can
    /// never hold a Dataverse license.
    /// </summary>
    public IPublicClientApplication BuildPublicClientForLogin(string? tenantId, CloudInstance cloud = CloudInstance.Public)
    {
        var authority = DataverseCloudMap.BuildAuthorityUri(cloud, tenantId);

        return PublicClientApplicationBuilder
            .Create(PublicClientId)
            .WithRedirectUri(PublicRedirectUri)
            .WithAuthority(authority.AbsoluteUri, validateAuthority: false)
            .Build();
    }

    /// <summary>
    /// Builds a confidential-client application. One of
    /// <see cref="ConfidentialClientMaterial.ClientSecret"/>,
    /// <see cref="ConfidentialClientMaterial.Certificate"/>, or
    /// <see cref="ConfidentialClientMaterial.AssertionCallback"/> must be set —
    /// the resolver/vault layer populates the right one based on the
    /// Credential's <see cref="CredentialKind"/>.
    /// </summary>
    public IConfidentialClientApplication BuildConfidentialClient(
        Connection connection,
        Credential credential,
        ConfidentialClientMaterial material)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(material);

        if (string.IsNullOrWhiteSpace(credential.ApplicationId))
            throw new InvalidOperationException(
                $"Credential '{credential.Id}' of kind {credential.Kind} requires ApplicationId for confidential flows.");
        if (material.IsEmpty)
            throw new InvalidOperationException(
                $"Credential '{credential.Id}' of kind {credential.Kind} has no client secret, certificate, or assertion callback.");

        var authority = ResolveAuthority(connection, credential);
        var builder = ConfidentialClientApplicationBuilder
            .Create(credential.ApplicationId)
            .WithAuthority(authority.AbsoluteUri, validateAuthority: false);

        if (!string.IsNullOrEmpty(material.ClientSecret))
            builder = builder.WithClientSecret(material.ClientSecret);
        else if (material.Certificate is not null)
            builder = builder.WithCertificate(material.Certificate, sendX5C: true);
        else if (material.AssertionCallback is not null)
            builder = builder.WithClientAssertion(ct => material.AssertionCallback(ct));

        return builder.Build();
    }

    /// <summary>
    /// Internal authority resolution: prefer an already-challenged authority
    /// stored by the caller (future), then explicit cloud on Connection /
    /// Credential, then inference from <c>EnvironmentUrl</c>.
    /// </summary>
    internal static Uri ResolveAuthority(Connection connection, Credential? credential = null)
    {
        var cloud =
            connection.Cloud
            ?? credential?.Cloud
            ?? (TryParseUri(connection.EnvironmentUrl, out var env)
                ? DataverseCloudMap.TryInferFromEnvironmentUrl(env!)
                : null)
            ?? CloudInstance.Public;

        var tenant = connection.TenantId ?? credential?.TenantId;
        return DataverseCloudMap.BuildAuthorityUri(cloud, tenant);
    }

    private static bool TryParseUri(string? raw, out Uri? uri)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            uri = null;
            return false;
        }
        return Uri.TryCreate(raw, UriKind.Absolute, out uri);
    }
}
