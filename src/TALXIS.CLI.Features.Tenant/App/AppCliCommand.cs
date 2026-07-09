using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant.App;

/// <summary>
/// Parent command for Entra application discovery and tenant-wide role assignment.
/// Usage: <c>txc tenant app [list|get|role]</c>
/// </summary>
[CliCommand(
    Name = "app",
    Description = "Discover Entra applications and manage their tenant-wide role assignments.",
    Children = new[]
    {
        typeof(AppListCliCommand),
        typeof(AppGetCliCommand),
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
/// Sub-resource for tenant-wide role assignments on an Entra application.
/// Usage: <c>txc tenant app role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Manage tenant-wide role assignments for an Entra application.",
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
