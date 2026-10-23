using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant.Group;

/// <summary>
/// Parent command for tenant-wide role assignment operations on an Entra group.
/// Usage: <c>txc tenant group role [list|add|remove]</c>
/// </summary>
/// <remarks>
/// There is deliberately no <c>list</c>/<c>get</c> sub-command here (unlike
/// <c>tenant user</c>/<c>tenant service-principal</c>): searching or resolving groups by
/// display name requires the Microsoft Graph <c>Group.Read.All</c>
/// permission, which is not pre-consented for this CLI's Entra app
/// registration in most tenants and we intentionally never prompt for extra
/// consent. Instead, the group is always identified by its Entra object id
/// (GUID) directly — the same approach used by <c>pac admin assign-group</c>.
/// Find the object id via the Entra admin center or
/// <c>az ad group show --group &lt;name&gt; --query id -o tsv</c>.
/// </remarks>
[CliCommand(
    Name = "group",
    Description = "Manage tenant role assignments for an Entra group, identified by its object id.",
    Children = new[]
    {
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
