using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.Role;

/// <summary>
/// Shows one tenant-assignable role from the catalog accepted by <c>--role</c> in tenant role-assignment commands.
/// Usage: <c>txc tenant role get --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one tenant role from the catalog accepted by --role in txc tenant service-principal/user/group role add/remove commands."
)]
public class RoleGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(RoleGetCliCommand));

    [CliOption(Name = "--role", Description = "Tenant role name or role id accepted by tenant role-assignment commands.", Required = true)]
    public string Role { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var role = await TenantRoleCommandSupport.GetRoleAsync(Profile, Role, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteData(role, RoleOutput.PrintDetail);
        return ExitSuccess;
    }
}
