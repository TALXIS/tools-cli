using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.App;

/// <summary>
/// Lists Entra applications available for tenant-wide role assignment.
/// Usage: <c>txc tenant app list [--filter &lt;name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Entra applications that can be targeted by txc tenant app role commands."
)]
public class AppListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only Entra applications whose display name starts with this value.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var rows = await TenantAppCommandSupport.ListAppsAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(rows, TenantAppCommandSupport.WriteAppTable);
        return ExitSuccess;
    }
}
