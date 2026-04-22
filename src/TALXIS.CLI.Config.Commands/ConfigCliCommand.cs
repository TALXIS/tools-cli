using DotMake.CommandLine;

namespace TALXIS.CLI.Config.Commands;

/// <summary>
/// Root <c>config</c> command group. Not yet wired into
/// <c>TxcCliCommand.Children</c> — that's the <c>wire-toplevel</c>
/// milestone (CONTRIBUTING taxonomy update must land first). Pinned in
/// the codebase as "invisible scaffolding" so the design is visible to
/// contributors and Children wiring still composes correctly when
/// wire-toplevel flips the switch.
/// </summary>
[CliCommand(
    Name = "config",
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
