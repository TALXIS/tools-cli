using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Parent command for Dataverse team operations.
/// Usage: <c>txc environment team [list|get|create|delete|member|role]</c>
/// </summary>
[CliCommand(
    Name = "team",
    Description = "Manage Dataverse teams, team membership, and team role assignments.",
    Children = new[]
    {
        typeof(TeamListCliCommand),
        typeof(TeamGetCliCommand),
        typeof(TeamCreateCliCommand),
        typeof(TeamDeleteCliCommand),
        typeof(TeamMemberCliCommand),
        typeof(TeamRoleCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TeamCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for Dataverse team membership operations.
/// Usage: <c>txc environment team member [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "member",
    Description = "List, add, or remove direct team members.",
    Children = new[]
    {
        typeof(TeamMemberListCliCommand),
        typeof(TeamMemberAddCliCommand),
        typeof(TeamMemberRemoveCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TeamMemberCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for Dataverse team role assignment operations.
/// Usage: <c>txc environment team role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "List, add, or remove security roles assigned to a team.",
    Children = new[]
    {
        typeof(TeamRoleListCliCommand),
        typeof(TeamRoleAddCliCommand),
        typeof(TeamRoleRemoveCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TeamRoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
