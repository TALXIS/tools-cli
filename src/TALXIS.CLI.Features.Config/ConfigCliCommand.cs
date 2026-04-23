using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Config;

/// <summary>
/// Root <c>config</c> command group — wired into
/// <c>TxcCliCommand.Children</c> so <c>txc config …</c> is invocable and
/// MCP discovers <c>config_*</c> tools. Alias <c>c</c> keeps day-to-day
/// typing short (e.g. <c>txc c p select customer-a-dev</c>).
/// </summary>
[CliCommand(
    Name = "config",
    Aliases = new[] { "c" },
    Description = "Manage txc profiles, connections, credentials, and settings.",
    Children = new[]
    {
        typeof(Auth.AuthCliCommand),
        typeof(Connection.ConnectionCliCommand),
        typeof(Profile.ProfileCliCommand),
        typeof(Setting.SettingCliCommand),
    }
)]
public class ConfigCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
