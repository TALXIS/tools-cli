using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.Role;

/// <summary>
/// Lists tenant-assignable roles that can be passed to <c>--role</c> in tenant role-assignment commands.
/// Usage: <c>txc tenant role list [--filter &lt;name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List tenant roles accepted by --role in txc tenant service-principal/user/group role add/remove commands."
)]
public class RoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(RoleListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only tenant roles whose name or role id contains this substring.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var roles = await TenantRoleCommandSupport.ListRolesAsync(Profile, Filter, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(roles, RoleOutput.PrintList);
        return ExitSuccess;
    }
}
