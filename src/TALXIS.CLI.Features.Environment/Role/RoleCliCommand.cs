using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Role;

/// <summary>
/// Parent command for browsing Dataverse security roles.
/// Usage: <c>txc environment role [list|get]</c>
/// The output helps you find values to pass to <c>--role</c> on other
/// <c>txc environment</c> commands.
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Browse Dataverse security roles in the target environment.",
    Children = new[] { typeof(RoleListCliCommand), typeof(RoleGetCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class RoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
