using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Bootstrapping;

/// <summary>The persisted credential and which sign-in flow produced it.</summary>
public sealed record CredentialSignInResult(InteractiveCredentialResult Credential, bool UsedDeviceCode);

/// <summary>
/// Signs the user in and persists the credential, choosing the flow once for
/// every entry point (<c>config auth login</c>, <c>config profile create --url</c>):
/// device code when requested or when no local browser can complete the
/// <c>localhost</c> redirect (Codespaces, SSH, containers), browser otherwise.
/// </summary>
public sealed class CredentialSignInService
{
    private readonly IInteractiveLoginService _interactiveLogin;
    private readonly IDeviceCodeLoginService _deviceCodeLogin;
    private readonly IBrowserAvailabilityProbe _browserProbe;
    private readonly ICredentialStore _credentials;
    private readonly IHeadlessDetector _headless;
    private readonly ILogger<CredentialSignInService> _logger;

    public CredentialSignInService(
        IInteractiveLoginService interactiveLogin,
        IDeviceCodeLoginService deviceCodeLogin,
        IBrowserAvailabilityProbe browserProbe,
        ICredentialStore credentials,
        IHeadlessDetector headless,
        ILogger<CredentialSignInService> logger)
    {
        _interactiveLogin = interactiveLogin ?? throw new ArgumentNullException(nameof(interactiveLogin));
        _deviceCodeLogin = deviceCodeLogin ?? throw new ArgumentNullException(nameof(deviceCodeLogin));
        _browserProbe = browserProbe ?? throw new ArgumentNullException(nameof(browserProbe));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _headless = headless ?? throw new ArgumentNullException(nameof(headless));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CredentialSignInResult> SignInAsync(
        string? tenantId,
        CloudInstance cloud,
        string? explicitAlias,
        bool forceDeviceCode,
        CancellationToken ct)
    {
        if (forceDeviceCode || !_browserProbe.IsBrowserAvailable)
        {
            _logger.LogInformation(
                "Starting device code sign-in (browser unavailable: {Reason})...",
                _browserProbe.UnavailableReason ?? "--device-code flag");

            var deviceCode = await DeviceCodeCredentialBootstrapper.AcquireAndPersistAsync(
                _deviceCodeLogin, _credentials, _headless, tenantId, cloud, explicitAlias, ct).ConfigureAwait(false);
            return new CredentialSignInResult(deviceCode, UsedDeviceCode: true);
        }

        _logger.LogInformation("Starting interactive sign-in...");

        var interactive = await InteractiveCredentialBootstrapper.AcquireAndPersistAsync(
            _interactiveLogin, _credentials, _headless, tenantId, cloud, explicitAlias, ct).ConfigureAwait(false);
        return new CredentialSignInResult(interactive, UsedDeviceCode: false);
    }
}
