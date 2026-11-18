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
/// <c>txc config auth login</c> — eager interactive sign-in, or secretless
/// managed-identity registration when <c>--identity</c> is specified.
/// Interactive login persists an <see cref="CredentialKind.InteractiveBrowser"/> or
/// <see cref="CredentialKind.DeviceCode"/> credential whose refresh token sits
/// in the shared MSAL cache; managed-identity registration persists endpoint
/// metadata only. No secret material is written to the txc credential-vault file.
/// </summary>
/// <remarks>
/// Interactive login fails fast with exit 1 in headless contexts — interactive browser is
/// never a permitted headless kind. See <see cref="HeadlessAuthRequiredException"/>.
/// When a local browser is not available (Codespaces, SSH, no DISPLAY),
/// the command automatically falls back to device code flow unless
/// <c>--device-code</c> was already specified.
/// </remarks>
[CliIdempotent]
[McpIgnore]
[CliCommand(
    Name = "login",
    Description = "Sign in interactively, or register the endpoint-default managed identity with --identity. Browser login falls back to device code in browser-isolated environments (Codespaces, SSH)."
)]
public class AuthLoginCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AuthLoginCliCommand));

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain. When omitted, the user picks an org in the browser.", Required = false)]
    public string? Tenant { get; set; }

    [CliOption(Name = "--alias", Description = "Credential alias. Default: signed-in UPN, or managed-identity with --identity (collision-resolved).", Required = false)]
    public string? Alias { get; set; }

    [CliOption(Name = "--cloud", Description = "Sovereign cloud. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--device-code", Description = "Use device code flow instead of browser login. Required when no local browser can reach localhost (Codespaces, SSH, containers).", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--managed-identity", Aliases = ["--managedIdentity", "--identity"], Description = "Register the host endpoint's default managed identity without interactive sign-in. Tokens are acquired when a resource is accessed.", Required = false)]
    public bool ManagedIdentity { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var cloud = Cloud ?? CloudInstance.Public;
        if (ManagedIdentity)
        {
            if (DeviceCode)
            {
                Logger.LogError("--identity and --device-code cannot be combined.");
                return ExitError;
            }

            var credential = await ManagedIdentityCredentialBootstrapper.RegisterAsync(
                TxcServices.Get<ICredentialStore>(), TxcServices.Get<IHeadlessDetector>(),
                Tenant, cloud, Alias, CancellationToken.None).ConfigureAwait(false);

            Logger.LogInformation("Managed identity credential '{Alias}' saved. Tokens will be acquired when a resource is accessed.",
                credential.Id);
            OutputFormatter.WriteData(new
            {
                id = credential.Id,
                kind = credential.Kind,
                tenantId = credential.TenantId,
                cloud,
                flow = "managed-identity",
            });
            return ExitSuccess;
        }

        var signIn = TxcServices.Get<CredentialSignInService>();

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
