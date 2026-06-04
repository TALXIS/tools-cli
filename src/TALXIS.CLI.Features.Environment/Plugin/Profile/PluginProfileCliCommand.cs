using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin.Profile;

[CliCommand(
    Name = "profile",
    Description = "Control the organization-wide plugin trace log level (organization.plugintracelogsetting). When enabled, plugin execution is written to the plugintracelog table for later inspection.",
    Children = new[]
    {
        typeof(PluginProfileStatusCliCommand),
        typeof(PluginProfileEnableCliCommand),
        typeof(PluginProfileDisableCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginProfileCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
