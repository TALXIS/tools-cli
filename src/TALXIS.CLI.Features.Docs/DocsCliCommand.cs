using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Docs;

[CliCommand(
    Description = "Knowledge base for TALXIS CLI and building software with it"
)]
public class DocsCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
