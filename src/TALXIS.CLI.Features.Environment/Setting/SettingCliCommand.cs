using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Setting;

/// <summary>
/// <c>txc environment setting</c> — manage environment settings across
/// all Power Platform backends (control plane, Organization table,
/// solution settings, copilot governance).
/// </summary>
[CliCommand(
    Name = "setting",
    Description = "Manage environment settings.",
    Children = new[] { typeof(SettingListCliCommand), typeof(SettingUpdateCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class SettingCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
