using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.UserSettings;

/// <summary>
/// <c>txc environment user-settings</c> - view and change the connected
/// user's personalization settings (timezone, locale, date/time formats).
/// </summary>
[CliCommand(
    Name = "user-settings",
    Description = "View and change the connected user's settings (timezone, locale, date/time formats).",
    Children = new[] { typeof(UserSettingsGetCliCommand), typeof(UserSettingsSetCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UserSettingsCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
