using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Config.Setting;

/// <summary>
/// <c>txc config setting</c> — tool-wide preference verbs. Backed by
/// <c>${TXC_CONFIG_DIR:-~/.txc}/config.json</c> (same file that carries
/// the active-profile pointer). The key surface is intentionally narrow
/// in v1 — see <see cref="SettingRegistry"/>.
/// </summary>
[CliCommand(
    Name = "setting",
    Description = "Manage tool-wide preferences (log, telemetry) in config.json.",
    Children = new[]
    {
        typeof(SettingSetCliCommand),
        typeof(SettingGetCliCommand),
        typeof(SettingListCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class SettingCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
