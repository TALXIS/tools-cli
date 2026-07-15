using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.ServicePrincipal;

/// <summary>
/// Lists tenant-wide role assignments for an Entra application.
/// Usage: <c>txc tenant service-principal role list --service-principal &lt;client-id-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List tenant-wide role assignments for an Entra application."
)]
public class ServicePrincipalRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleListCliCommand));

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListRolesAsync();

    private async Task<int> ExecuteListRolesAsync()
    {
        try
        {
            var rows = await TenantServicePrincipalCommandSupport.ListAssignmentsAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, TenantServicePrincipalCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (TenantPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
