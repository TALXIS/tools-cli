using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Parent command for Dataverse application-user operations.
/// Application users are service principals represented by <c>systemuser</c>
/// rows with an application client ID.
/// Usage: <c>txc environment app [list|get|create|update|delete|role]</c>
/// </summary>
[CliCommand(
    Name = "app",
    Description = "Manage Dataverse application users (service principals) in the current environment.",
    Children = new[]
    {
        typeof(AppListCliCommand),
        typeof(AppGetCliCommand),
        typeof(AppCreateCliCommand),
        typeof(AppUpdateCliCommand),
        typeof(AppDeleteCliCommand),
        typeof(AppRoleCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class AppCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for Dataverse security-role assignments on an application user.
/// Usage: <c>txc environment app role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Manage Dataverse security roles assigned to an application user.",
    Children = new[]
    {
        typeof(AppRoleListCliCommand),
        typeof(AppRoleAddCliCommand),
        typeof(AppRoleRemoveCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class AppRoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
