using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Security.Role;

/// <summary>
/// Parent command for the tenant role catalog and Dataverse environment role catalog.
/// Usage: <c>txc security role [list|get]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Browse the tenant role catalog when no environment is resolved, or the Dataverse security-role catalog when --environment is provided or resolved from the active connection.",
    Children = new[]
    {
        typeof(RoleListCliCommand),
        typeof(RoleGetCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class RoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
