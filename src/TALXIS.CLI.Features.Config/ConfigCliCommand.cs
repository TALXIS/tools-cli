using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Config;

/// <summary>
/// Root <c>config</c> command group — wired into
/// <c>TxcCliCommand.Children</c> so <c>txc config …</c> is invocable and
/// MCP discovers <c>config_*</c> tools.
/// </summary>
[CliCommand(
    Name = "config",
    Description = "Manage txc profiles, connections, credentials, and settings.",
    Children = new[]
    {
        typeof(ConfigClearCliCommand),
        typeof(Auth.AuthCliCommand),
        typeof(Connection.ConnectionCliCommand),
        typeof(Profile.ProfileCliCommand),
        typeof(Setting.SettingCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConfigCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
