using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Security.Team;

/// <summary>
/// Parent command for Dataverse team operations under txc security.
/// Usage: <c>txc security team [list|get|create|delete|member|role]</c>
/// </summary>
[CliCommand(
    Name = "team",
    Description = "Manage Dataverse teams, team membership, and team role assignments. Every team command requires --environment or an active environment connection because teams have no tenant-wide security equivalent.",
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

[CliCommand(
    Name = "member",
    Description = "List, add, or remove direct Dataverse team members in the resolved environment.",
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

[CliCommand(
    Name = "role",
    Description = "List, add, or remove Dataverse security roles assigned to a team in the resolved environment.",
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
