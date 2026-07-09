using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.User;

/// <summary>
/// Lists tenant roles assigned to an Entra user.
/// Usage: <c>txc tenant user role list --user &lt;upn-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List tenant roles assigned to an Entra user in the connected tenant."
)]
public class UserRoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleListCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object id.", Required = true)]
    public string User { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteListRolesAsync();

    private async Task<int> ExecuteListRolesAsync()
    {
        try
        {
            var assignments = await UserCommandSupport.ListRolesAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteList(assignments, TenantPrincipalCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }
        catch (Exception ex) when (TenantPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
