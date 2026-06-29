using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.Msal;

/// <summary>
/// MSAL-backed <see cref="IInteractiveLoginService"/>. Builds a fresh
/// public-client application per call — MSAL's cache is shared via
/// <see cref="MsalTokenCacheBinder"/>, so the new client instance
/// benefits from earlier sign-ins.
/// </summary>
/// <remarks>
/// Scopes: <c>openid profile offline_access</c> only. This is an
/// identity-warming sign-in: we want the refresh token persisted in the
/// MSAL cache so later profile-bound commands can acquire Dataverse
/// access tokens silently. Running the Dataverse <c>//.default</c> scope
/// at this point would force the user to name an env that may not exist
/// yet in their config.
///
/// A custom <see cref="SystemWebViewOptions.OpenBrowserAsync"/> prints
/// the auth URL to the terminal on platforms where <c>xdg-open</c> may
/// not be available (SSH, containers). This makes VS Code Desktop tunnel
/// scenarios work — the user clicks the printed URL and VS Code forwards
/// the localhost redirect back into the container.
/// </remarks>
public sealed class DataverseInteractiveLoginService : IInteractiveLoginService
{
    private static readonly string[] SignInScopes = ["openid", "profile", "offline_access"];

    private readonly MsalClientFactory _factory;
    private readonly MsalTokenCacheBinder _cacheBinder;
    private readonly ILogger _logger;

    public DataverseInteractiveLoginService(
        MsalClientFactory factory,
        MsalTokenCacheBinder cacheBinder,
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

        var systemWebViewOptions = new SystemWebViewOptions
        {
            // Custom browser launcher that prints the URL to the terminal.
            // On Linux without xdg-open (SSH, containers) the default MSAL
            // behaviour would throw — this ensures the user always sees the URL
            // and can click it (VS Code Desktop tunnels localhost back).
            OpenBrowserAsync = uri =>
            {
                // Use AbsoluteUri (fully percent-encoded) so the URL is valid when
                // logged (copyable into a browser) and when passed to the OS launcher.
                // Uri.ToString() decodes %20 to spaces, which makes the URL invalid;
                // macOS open then re-encodes everything, double-encoding %3A → %253A.
                var absoluteUrl = uri.AbsoluteUri;
                _logger.LogWarning("Open this URL in your browser to sign in:\n  {AuthUrl}", absoluteUrl);
                TryLaunchBrowser(absoluteUrl);
                return Task.CompletedTask;
            }
        };

        AuthenticationResult result = await app
            .AcquireTokenInteractive(SignInScopes)
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(systemWebViewOptions)
            .ExecuteAsync(ct)
            .ConfigureAwait(false);

        var upn = result.Account?.Username;
        if (string.IsNullOrWhiteSpace(upn))
            throw new InvalidOperationException(
                "Sign-in succeeded but MSAL did not return a UPN. Try again with --alias to provide one explicitly.");

        return new InteractiveLoginResult(
            upn,
            result.TenantId,
            result.Account?.HomeAccountId?.Identifier,
            MsalClientFactory.PublicClientId);
    }

    /// <summary>
    /// Best-effort browser launch. Failures are silently swallowed — the URL
    /// is already printed to the terminal for manual use.
    /// </summary>
    private void TryLaunchBrowser(string url)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            }
            else
            {
                // Pass the URL as a single element in ArgumentList so that spaces
                // in query-string values (e.g. "scope=openid profile offline_access")
                // are not split into separate arguments by the shell.
                psi = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open");
                psi.ArgumentList.Add(url);
            }
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Browser launch failed (expected in headless/container environments).");
        }
    }
}
