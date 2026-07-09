using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant;

[CliCommand(
    Name = "tenant",
    Description = "Discover and manage tenant-wide resources and role assignments.",
    Children = new[] { typeof(Role.RoleCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TenantCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
