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
/// <c>txc config auth login</c> — eager interactive browser sign-in.
/// Persists an <see cref="CredentialKind.InteractiveBrowser"/>
/// credential whose refresh token sits in the shared MSAL cache; no
/// secret material is written to the txc credential-vault file.
/// </summary>
/// <remarks>
/// Fails fast with exit 1 in headless contexts — interactive browser is
/// never a permitted headless kind. See <see cref="HeadlessAuthRequiredException"/>.
/// </remarks>
[McpIgnore]
[CliCommand(
    Name = "login",
    Description = "Interactive browser sign-in. Persists a credential named after the UPN (override with --alias)."
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

    protected override async Task<int> ExecuteAsync()
    {
        var login = TxcServices.Get<IInteractiveLoginService>();
        var store = TxcServices.Get<ICredentialStore>();
        var headless = TxcServices.Get<IHeadlessDetector>();
        var cloud = Cloud ?? CloudInstance.Public;

        Logger.LogInformation("Starting interactive sign-in...");
        var result = await InteractiveCredentialBootstrapper.AcquireAndPersistAsync(
            login, store, headless, Tenant, cloud, Alias, CancellationToken.None).ConfigureAwait(false);

        Logger.LogInformation("Signed in as {Upn} (tenant {Tenant}). Credential '{Alias}' saved.",
            result.Upn, result.TenantId, result.Credential.Id);

        OutputFormatter.WriteData(new { id = result.Credential.Id, upn = result.Upn, tenantId = result.TenantId, cloud });
        return ExitSuccess;
    }
}
