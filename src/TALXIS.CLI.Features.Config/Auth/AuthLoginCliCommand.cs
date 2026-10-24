using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
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
/// When a local browser is not available (Codespaces, SSH, no DISPLAY),
/// the command automatically falls back to device code flow unless
/// <c>--device-code</c> was already specified.
/// </remarks>
[CliIdempotent]
[McpIgnore]
[CliCommand(
    Name = "login",
    Description = "Interactive sign-in. Uses browser login by default; falls back to device code in browser-isolated environments (Codespaces, SSH)."
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

    [CliOption(Name = "--device-code", Description = "Use device code flow instead of browser login. Required when no local browser can reach localhost (Codespaces, SSH, containers).", Required = false)]
    public bool DeviceCode { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var signIn = TxcServices.Get<CredentialSignInService>();
        var cloud = Cloud ?? CloudInstance.Public;

        var outcome = await signIn.SignInAsync(Tenant, cloud, Alias, DeviceCode, CancellationToken.None).ConfigureAwait(false);
        var result = outcome.Credential;

        Logger.LogInformation("Signed in as {Upn} (tenant {Tenant}). Credential '{Alias}' saved.",
            result.Upn, result.TenantId, result.Credential.Id);

        OutputFormatter.WriteData(new
        {
            id = result.Credential.Id,
            upn = result.Upn,
            tenantId = result.TenantId,
            cloud,
            flow = outcome.UsedDeviceCode ? "device-code" : "interactive-browser",
        });
        return ExitSuccess;
    }
}
