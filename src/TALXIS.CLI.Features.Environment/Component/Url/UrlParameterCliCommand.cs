using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component.Url;

[CliCommand(
    Name = "parameter",
    Description = "Discover URL parameters accepted by each component type.",
    Children = new[] { typeof(UrlParameterListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UrlParameterCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
