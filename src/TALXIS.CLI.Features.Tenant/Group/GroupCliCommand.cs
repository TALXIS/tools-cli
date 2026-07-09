using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant.Group;

/// <summary>
/// Parent command for tenant-wide Entra group discovery and role assignment operations.
/// Usage: <c>txc tenant group [list|get|role]</c>
/// </summary>
[CliCommand(
    Name = "group",
    Description = "Discover Entra groups and manage their tenant role assignments.",
    Children = new[]
    {
        typeof(GroupListCliCommand),
        typeof(GroupGetCliCommand),
        typeof(GroupRoleCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class GroupCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for tenant-wide role assignments on an Entra group.
/// Usage: <c>txc tenant group role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Manage tenant role assignments for an Entra group.",
    Children = new[]
    {
        typeof(GroupRoleListCliCommand),
        typeof(GroupRoleAddCliCommand),
        typeof(GroupRoleRemoveCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class GroupRoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
