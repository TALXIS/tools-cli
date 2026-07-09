using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.App;

/// <summary>
/// Lists tenant-wide role assignments for an Entra application.
/// Usage: <c>txc tenant app role list --app &lt;client-id-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List tenant-wide role assignments for an Entra application."
)]
public class AppRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppRoleListCliCommand));

    [CliOption(Name = "--app", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string App { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListRolesAsync();

    private async Task<int> ExecuteListRolesAsync()
    {
        try
        {
            var rows = await TenantAppCommandSupport.ListAssignmentsAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(rows, TenantAppCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (TenantAppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
