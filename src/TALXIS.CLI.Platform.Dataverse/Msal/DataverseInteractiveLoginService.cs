using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Platform.Dataverse.Msal;

/// <summary>
/// MSAL-backed <see cref="IInteractiveLoginService"/>. Builds a fresh
/// public-client application per call — MSAL's cache is shared via
/// <see cref="DataverseTokenCacheBinder"/>, so the new client instance
/// benefits from earlier sign-ins.
/// </summary>
/// <remarks>
/// Scopes: <c>openid profile offline_access</c> only. This is an
/// identity-warming sign-in: we want the refresh token persisted in the
/// MSAL cache so later profile-bound commands can acquire Dataverse
/// access tokens silently. Running the Dataverse <c>//.default</c> scope
/// at this point would force the user to name an env that may not exist
/// yet in their config.
/// </remarks>
public sealed class DataverseInteractiveLoginService : IInteractiveLoginService
{
    private static readonly string[] SignInScopes = ["openid", "profile", "offline_access"];

    private readonly DataverseMsalClientFactory _factory;
    private readonly DataverseTokenCacheBinder _cacheBinder;
    private readonly ILogger _logger;

    public DataverseInteractiveLoginService(
        DataverseMsalClientFactory factory,
        DataverseTokenCacheBinder cacheBinder,
        ILogger<DataverseInteractiveLoginService>? logger = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _cacheBinder = cacheBinder ?? throw new ArgumentNullException(nameof(cacheBinder));
        _logger = logger ?? NullLogger<DataverseInteractiveLoginService>.Instance;
    }

    public async Task<InteractiveLoginResult> LoginAsync(
        string? tenantId,
        CloudInstance cloud,
        CancellationToken ct)
    {
        var app = _factory.BuildPublicClientForLogin(tenantId, cloud);
        _cacheBinder.Attach(app.UserTokenCache);

        _logger.LogInformation(
            "Launching browser for interactive sign-in (tenant={Tenant}, cloud={Cloud}).",
            tenantId ?? "organizations", cloud);

        AuthenticationResult result = await app
            .AcquireTokenInteractive(SignInScopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(ct)
            .ConfigureAwait(false);

        var upn = result.Account?.Username;
        if (string.IsNullOrWhiteSpace(upn))
            throw new InvalidOperationException(
                "Sign-in succeeded but MSAL did not return a UPN. Try again with --alias to provide one explicitly.");

        return new InteractiveLoginResult(upn, result.TenantId);
    }
}
