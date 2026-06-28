using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.Msal;

/// <summary>
/// MSAL-backed <see cref="IDeviceCodeLoginService"/>. Uses the device code
/// flow (<c>AcquireTokenWithDeviceCode</c>) so the user authenticates in an
/// external browser by visiting <c>https://microsoft.com/devicelogin</c>.
/// </summary>
/// <remarks>
/// This is the correct flow for browser-isolated environments — GitHub
/// Codespaces (browser-based), SSH sessions, containers without a display.
/// The localhost redirect used by interactive browser login is not reachable
/// in these environments; device code needs no redirect at all.
///
/// Scopes: <c>openid profile offline_access</c> only — same identity-warming
/// strategy as <see cref="DataverseInteractiveLoginService"/>.
/// </remarks>
public sealed class DataverseDeviceCodeLoginService : IDeviceCodeLoginService
{
    private static readonly string[] SignInScopes = ["openid", "profile", "offline_access"];

    private readonly MsalClientFactory _factory;
    private readonly MsalTokenCacheBinder _cacheBinder;
    private readonly ILogger _logger;

    public DataverseDeviceCodeLoginService(
        MsalClientFactory factory,
        MsalTokenCacheBinder cacheBinder,
        ILogger<DataverseDeviceCodeLoginService>? logger = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _cacheBinder = cacheBinder ?? throw new ArgumentNullException(nameof(cacheBinder));
        _logger = logger ?? NullLogger<DataverseDeviceCodeLoginService>.Instance;
    }

    public async Task<InteractiveLoginResult> LoginAsync(
        string? tenantId,
        CloudInstance cloud,
        CancellationToken ct)
    {
        var app = _factory.BuildPublicClientForLogin(tenantId, cloud);
        _cacheBinder.Attach(app.UserTokenCache);

        _logger.LogInformation(
            "Starting device code sign-in (tenant={Tenant}, cloud={Cloud}).",
            tenantId ?? "organizations", cloud);

        AuthenticationResult result = await app
            .AcquireTokenWithDeviceCode(SignInScopes, deviceCodeResult =>
            {
                // MSAL composes a full user-facing message like:
                // "To sign in, use a web browser to open the page
                //  https://microsoft.com/devicelogin and enter the code ABCDEFG"
                _logger.LogWarning("{DeviceCodeMessage}", deviceCodeResult.Message);
                return Task.CompletedTask;
            })
            .ExecuteAsync(ct)
            .ConfigureAwait(false);

        var upn = result.Account?.Username;
        if (string.IsNullOrWhiteSpace(upn))
            throw new InvalidOperationException(
                "Device code sign-in succeeded but MSAL did not return a UPN. " +
                "Try again with --alias to provide one explicitly.");

        return new InteractiveLoginResult(
            upn,
            result.TenantId,
            result.Account?.HomeAccountId?.Identifier,
            MsalClientFactory.PublicClientId);
    }
}
