using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component.Url;

[CliCommand(
    Name = "url",
    Description = "Build and open URLs for component editors, apps, records, and reports in the live environment.",
    Children = new[] { typeof(UrlGetCliCommand), typeof(UrlOpenCliCommand), typeof(UrlParameterCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UrlCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
