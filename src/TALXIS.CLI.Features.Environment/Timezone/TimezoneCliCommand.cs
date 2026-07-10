using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Timezone;

/// <summary>
/// <c>txc environment timezone</c> - look up Dataverse timezones for use with
/// <c>user-settings set --timezone</c>.
/// </summary>
[CliCommand(
    Name = "timezone",
    Description = "Look up Dataverse timezones.",
    Children = new[] { typeof(TimezoneListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TimezoneCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
