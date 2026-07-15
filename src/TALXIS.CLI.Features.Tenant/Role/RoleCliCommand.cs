using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant.Role;

/// <summary>
/// Parent command for the tenant role catalog.
/// Usage: <c>txc tenant role [list|get]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Browse the tenant role catalog accepted by --role in txc tenant service-principal/user/group role add/remove commands.",
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
