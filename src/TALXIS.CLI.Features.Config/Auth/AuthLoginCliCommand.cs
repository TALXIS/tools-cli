using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Auth;

/// <summary>
/// <c>txc config auth login</c> — eager interactive sign-in.
/// Persists an <see cref="CredentialKind.InteractiveBrowser"/> or
/// <see cref="CredentialKind.DeviceCode"/> credential whose refresh token
/// sits in the shared MSAL cache; no secret material is written to the
/// txc credential-vault file.
/// </summary>
/// <remarks>
/// Fails fast with exit 1 in headless contexts — interactive browser is
/// never a permitted headless kind. See <see cref="HeadlessAuthRequiredException"/>.
/// Sign-in is always a deliberate, manual action taken by a human in their
/// own terminal — this command never guesses on the caller's behalf. When a
/// local browser is not available (Codespaces, SSH, no DISPLAY) it does
/// <b>not</b> silently switch to device code; it fails fast and tells the
/// human to re-run with <c>--device-code</c> themselves. This mirrors how
/// real scripted workflows already use this command (they always pass
/// <c>--device-code</c> explicitly rather than relying on auto-detection).
/// </remarks>
[CliIdempotent]
[McpIgnore]
[CliCommand(
    Name = "login",
    Description = "Interactive sign-in, run manually by a human in their own terminal — never by an automated caller. Uses browser login by default; pass --device-code yourself when no local browser can reach localhost (Codespaces, SSH, containers)."
)]
public class AuthLoginCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AuthLoginCliCommand));

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain. When omitted, the user picks an org in the browser.", Required = false)]
    public string? Tenant { get; set; }

    [CliOption(Name = "--alias", Description = "Credential alias. Default: signed-in UPN (collision-resolved).", Required = false)]
    public string? Alias { get; set; }

    [CliOption(Name = "--cloud", Description = "Sovereign cloud. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--device-code", Description = "Use device code flow instead of browser login. Pass this yourself — deliberately, as the human running this command — when no local browser can reach localhost (Codespaces, SSH, containers). Never inferred automatically.", Required = false)]
    public bool DeviceCode { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<ICredentialStore>();
        var headless = TxcServices.Get<IHeadlessDetector>();
        var browserProbe = TxcServices.Get<IBrowserAvailabilityProbe>();
        var cloud = Cloud ?? CloudInstance.Public;

        // Sign-in is always a deliberate, manual choice made by a human.
        // Never silently switch flows on the caller's behalf: if no local
        // browser is reachable and --device-code wasn't explicitly passed,
        // fail fast instead of starting an unattended device-code prompt
        // that nobody will complete (see HeadlessAuthRequiredException for
        // the equivalent fully-headless case).
        if (!DeviceCode && !browserProbe.IsBrowserAvailable)
        {
            Logger.LogError(
                "No local browser is available ({Reason}). Interactive sign-in requires a human to run it " +
                "deliberately in their own terminal. Re-run this command yourself with '--device-code' to sign in " +
                "using the device code flow. This is never chosen automatically.",
                browserProbe.UnavailableReason ?? "unknown reason");
            return ExitError;
        }

        if (DeviceCode)
        {
            var deviceCodeLogin = TxcServices.Get<IDeviceCodeLoginService>();
            Logger.LogInformation("Starting device code sign-in (requested via --device-code; browser reachable: {BrowserAvailable})...",
                browserProbe.IsBrowserAvailable);

            var result = await DeviceCodeCredentialBootstrapper.AcquireAndPersistAsync(
                deviceCodeLogin, store, headless, Tenant, cloud, Alias, CancellationToken.None).ConfigureAwait(false);

            Logger.LogInformation("Signed in as {Upn} (tenant {Tenant}). Credential '{Alias}' saved.",
                result.Upn, result.TenantId, result.Credential.Id);

            OutputFormatter.WriteData(new { id = result.Credential.Id, upn = result.Upn, tenantId = result.TenantId, cloud, flow = "device-code" });
            return ExitSuccess;
        }
        else
        {
            var login = TxcServices.Get<IInteractiveLoginService>();
            Logger.LogInformation("Starting interactive sign-in...");

            var result = await InteractiveCredentialBootstrapper.AcquireAndPersistAsync(
                login, store, headless, Tenant, cloud, Alias, CancellationToken.None).ConfigureAwait(false);

            Logger.LogInformation("Signed in as {Upn} (tenant {Tenant}). Credential '{Alias}' saved.",
                result.Upn, result.TenantId, result.Credential.Id);

            OutputFormatter.WriteData(new { id = result.Credential.Id, upn = result.Upn, tenantId = result.TenantId, cloud, flow = "interactive-browser" });
            return ExitSuccess;
        }
    }
}
