using DotMake.CommandLine;

namespace TALXIS.CLI.Config.Commands.Profile;

/// <summary>
/// <c>txc config profile</c> — Profiles are the "context": a named
/// binding of one Connection (the "where") to one Credential (the
/// "who"). Profiles are the only primitive that leaf commands consume
/// via <c>--profile</c>.
/// </summary>
[CliCommand(
    Name = "profile",
    Aliases = new[] { "p" },
    Description = "Manage profiles (bind one auth to one connection).",
    Children = new[]
    {
        typeof(ProfileCreateCliCommand),
        typeof(ProfileListCliCommand),
        typeof(ProfileShowCliCommand),
        typeof(ProfileUpdateCliCommand),
        typeof(ProfileSelectCliCommand),
        typeof(ProfileDeleteCliCommand),
    }
)]
public class ProfileCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
