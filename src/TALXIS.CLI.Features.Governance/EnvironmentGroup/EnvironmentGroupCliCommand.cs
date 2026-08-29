using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Parent command for tenant-level environment groups — Microsoft's own
/// "folder for your environments" concept, used to organize managed
/// environments and serve as the attachment point for governance rules
/// (<c>txc governance policy-rule</c>) and role assignments.
/// Usage: <c>txc governance environment-group [list|get|create|update|delete|environment]</c>
/// </summary>
[CliCommand(
    Name = "environment-group",
    Description = "Manage tenant-level environment groups: folders that organize managed environments and serve as the attachment point for governance rules (txc governance policy-rule) and role assignments. Typical sequence: create a group, add member environments (environment add), grant access via role assignments (role add), then create and assign policy rules to the group (txc governance policy-rule assign --environment-group).",
    Children = new[]
    {
        typeof(EnvironmentGroupListCliCommand),
        typeof(EnvironmentGroupGetCliCommand),
        typeof(EnvironmentGroupCreateCliCommand),
        typeof(EnvironmentGroupUpdateCliCommand),
        typeof(EnvironmentGroupDeleteCliCommand),
        typeof(EnvironmentGroupEnvironmentCliCommand),
        typeof(EnvironmentGroupRoleCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EnvironmentGroupCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for RBAC role assignments held directly on an environment
/// group. These grants apply to every environment currently in the group
/// and every environment added to it later - the same built-in roles
/// (Owner, Contributor, Reader, RBAC Administrator) used by
/// <c>txc security ... role</c>.
/// Usage: <c>txc governance environment-group role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "List, grant, or revoke RBAC role assignments on an environment group. Assignments apply to every environment currently in the group and every environment added to it later.",
    Children = new[]
    {
        typeof(EnvironmentGroupRoleListCliCommand),
        typeof(EnvironmentGroupRoleAddCliCommand),
        typeof(EnvironmentGroupRoleRemoveCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EnvironmentGroupRoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for environment-group membership.
/// Usage: <c>txc governance environment-group environment [add|remove]</c>
/// </summary>
[CliCommand(
    Name = "environment",
    Description = "Add or remove member environments from an environment group. Only managed environments can belong to a group; each environment can belong to at most one group at a time.",
    Children = new[]
    {
        typeof(EnvironmentGroupEnvironmentAddCliCommand),
        typeof(EnvironmentGroupEnvironmentRemoveCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EnvironmentGroupEnvironmentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
