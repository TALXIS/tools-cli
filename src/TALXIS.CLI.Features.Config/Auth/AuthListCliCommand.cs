using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Auth;

/// <summary>
/// <c>txc config auth list</c> — dumps all stored credentials as a JSON
/// array on stdout. Secrets are held in the OS vault and are never
/// touched by this command.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List all stored credentials as JSON."
)]
public class AuthListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AuthListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<ICredentialStore>();
        IReadOnlyList<Credential> creds = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);

        // Project to a deterministic shape: id, kind, tenantId, applicationId, cloud, description.
        // SecretRef is implied by kind — the secret itself never leaves the vault.
        var projected = creds.Select(c => new
        {
            id = c.Id,
            kind = c.Kind,
            tenantId = c.TenantId,
            applicationId = c.ApplicationId,
            cloud = c.Cloud,
            description = c.Description,
        }).ToList();

        OutputFormatter.WriteList(projected);
        return ExitSuccess;
    }
}
