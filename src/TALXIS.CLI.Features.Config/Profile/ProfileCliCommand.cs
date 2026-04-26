using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Config.Profile;

/// <summary>
/// <c>txc config profile</c> — Profiles are the "context": a named
/// binding of one Connection (the "where") to one Credential (the
/// "who"). Profiles are the only primitive that leaf commands consume
/// via <c>--profile</c>.
/// </summary>
[CliCommand(
    Name = "profile",
    Description = "Manage profiles (bind one auth to one connection).",
    Children = new[]
    {
        typeof(ProfileCreateCliCommand),
        typeof(ProfileListCliCommand),
        typeof(ProfileShowCliCommand),
        typeof(ProfileUpdateCliCommand),
        typeof(ProfileSelectCliCommand),
        typeof(ProfilePinCliCommand),
        typeof(ProfileUnpinCliCommand),
        typeof(ProfileValidateCliCommand),
        typeof(ProfileDeleteCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ProfileCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
