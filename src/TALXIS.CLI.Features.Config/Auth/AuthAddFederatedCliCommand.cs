using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Auth;

/// <summary>
/// <c>txc config auth add-federated</c> — register a workload-identity
/// federation credential (tenant + app id, no secret). At token
/// acquisition time, txc auto-detects the OIDC assertion source from
/// GitHub Actions (<c>ACTIONS_ID_TOKEN_REQUEST_URL</c> /
/// <c>ACTIONS_ID_TOKEN_REQUEST_TOKEN</c>) or Azure DevOps
/// System.AccessToken-backed environment variables.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add-federated",
    Aliases = ["add-workload-identity"],
    Description = "Register a workload-identity federation credential. At token acquisition time, txc auto-detects the OIDC assertion from GitHub Actions (ACTIONS_ID_TOKEN_REQUEST_URL/TOKEN) or Azure DevOps (SYSTEM_ACCESSTOKEN-backed) environment variables."
)]
public class AuthAddFederatedCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AuthAddFederatedCliCommand));

    [CliOption(Name = "--alias", Description = "Credential alias used to reference this federated credential.", Required = true)]
    public string Alias { get; set; } = string.Empty;

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain.", Required = true)]
    public string Tenant { get; set; } = string.Empty;

    [CliOption(Name = "--application-id", Aliases = ["--app-id", "--client-id"], Description = "Entra application (client) id.", Required = true)]
    public string ApplicationId { get; set; } = string.Empty;

    [CliOption(Name = "--cloud", Description = "Sovereign cloud. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--description", Description = "Free-form label shown in 'config auth list'.", Required = false)]
    public string? Description { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var headless = TxcServices.Get<IHeadlessDetector>();
        headless.EnsureKindAllowed(CredentialKind.WorkloadIdentityFederation);

        var alias = Alias.Trim();
        if (string.IsNullOrEmpty(alias))
        {
            Logger.LogError("--alias must not be empty.");
            return ExitError;
        }

        var credential = new Credential
        {
            Id = alias,
            Kind = CredentialKind.WorkloadIdentityFederation,
            TenantId = Tenant.Trim(),
            ApplicationId = ApplicationId.Trim(),
            Cloud = Cloud ?? CloudInstance.Public,
            Description = Description,
        };

        var store = TxcServices.Get<ICredentialStore>();
        await store.UpsertAsync(credential, CancellationToken.None).ConfigureAwait(false);

        Logger.LogInformation("Saved federated credential '{Alias}' (app {AppId}, tenant {Tenant}).",
            alias, credential.ApplicationId, credential.TenantId);

        OutputFormatter.WriteData(new
        {
            id = credential.Id,
            kind = credential.Kind,
            tenantId = credential.TenantId,
            applicationId = credential.ApplicationId,
            cloud = credential.Cloud,
            description = credential.Description,
        });
        return ExitSuccess;
    }
}
