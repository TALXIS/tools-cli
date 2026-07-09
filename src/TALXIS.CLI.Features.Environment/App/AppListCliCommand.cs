using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Lists Dataverse application users.
/// Usage: <c>txc environment app list [--enabled|--disabled|--all]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Dataverse application users. Defaults to enabled-only when no state flag is provided."
)]
public class AppListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppListCliCommand));

    [CliOption(Name = "--enabled", Description = "List only enabled application users. This is the default when no state flag is provided.", Required = false)]
    public bool Enabled { get; set; }

    [CliOption(Name = "--disabled", Description = "List only disabled application users.", Required = false)]
    public bool Disabled { get; set; }

    [CliOption(Name = "--all", Description = "List both enabled and disabled application users.", Required = false)]
    public bool All { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!AppCommandSupport.TryResolveStateFilter(Enabled, Disabled, All, Logger, out var filter))
            return Task.FromResult(ExitValidationError);

        return ExecuteListAsync(filter);
    }

    private async Task<int> ExecuteListAsync(DataverseSecurityPrincipalStateFilter filter)
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();
            var rows = await service.ListAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, AppCommandSupport.WriteAppTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (AppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
