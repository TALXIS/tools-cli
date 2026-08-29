using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Security;

[CliCommand(
    Name = "security",
    Description = "Discover and manage tenant-wide security resources, or switch to Dataverse environment-scoped security commands with --environment or an active environment connection.",
    Children = new[] { typeof(Role.RoleCliCommand), typeof(ServicePrincipal.ServicePrincipalCliCommand), typeof(User.UserCliCommand), typeof(Team.TeamCliCommand), typeof(Group.GroupCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class SecurityCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
