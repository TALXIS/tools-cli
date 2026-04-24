using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Setting;

/// <summary>
/// <c>txc environment setting</c> — manage environment management settings
/// via the Power Platform control plane API.
/// </summary>
[CliCommand(
    Name = "setting",
    Description = "Manage environment management settings (Power Platform control plane).",
    Children = new[] { typeof(SettingListCliCommand), typeof(SettingUpdateCliCommand) }
)]
public class SettingCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
