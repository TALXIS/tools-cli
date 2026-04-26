using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Publisher;

[CliCommand(
    Name = "publisher",
    Description = "Manage solution publishers in the target environment.",
    Children = new[] { typeof(PublisherListCliCommand), typeof(PublisherShowCliCommand), typeof(PublisherCreateCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PublisherCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
