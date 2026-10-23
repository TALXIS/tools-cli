using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Lists security roles assigned to a Dataverse service principal.
/// Usage: <c>txc environment service-principal role list --service-principal &lt;client-id-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List security roles assigned to a Dataverse service principal."
)]
public class ServicePrincipalRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleListCliCommand));

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListRolesAsync();

    private async Task<int> ExecuteListRolesAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var rows = await service.ListRolesAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, ServicePrincipalCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
