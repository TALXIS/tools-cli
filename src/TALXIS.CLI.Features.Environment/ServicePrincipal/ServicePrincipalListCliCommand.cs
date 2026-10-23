using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Lists Dataverse service principals.
/// Usage: <c>txc environment service-principal list [--enabled|--disabled|--all]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Dataverse service principals. Defaults to enabled-only when no state flag is provided."
)]
public class ServicePrincipalListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalListCliCommand));

    [CliOption(Name = "--enabled", Description = "List only enabled service principals. This is the default when no state flag is provided.", Required = false)]
    public bool Enabled { get; set; }

    [CliOption(Name = "--disabled", Description = "List only disabled service principals.", Required = false)]
    public bool Disabled { get; set; }

    [CliOption(Name = "--all", Description = "List both enabled and disabled service principals.", Required = false)]
    public bool All { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!ServicePrincipalCommandSupport.TryResolveStateFilter(Enabled, Disabled, All, Logger, out var filter))
            return Task.FromResult(ExitValidationError);

        return ExecuteListAsync(filter);
    }

    private async Task<int> ExecuteListAsync(DataverseSecurityPrincipalStateFilter filter)
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var rows = await service.ListAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, ServicePrincipalCommandSupport.WriteAppTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
