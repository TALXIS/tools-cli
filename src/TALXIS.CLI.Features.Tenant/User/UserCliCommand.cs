using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Tenant.User;

/// <summary>
/// Parent command for tenant-wide Entra user discovery and role assignment operations.
/// Usage: <c>txc tenant user [list|get|role]</c>
/// </summary>
[CliCommand(
    Name = "user",
    Description = "Discover Entra users and manage their tenant role assignments.",
    Children = new[]
    {
        typeof(UserListCliCommand),
        typeof(UserGetCliCommand),
        typeof(UserRoleCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UserCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for tenant-wide role assignments on an Entra user.
/// Usage: <c>txc tenant user role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "Manage tenant role assignments for an Entra user.",
    Children = new[]
    {
        typeof(UserRoleListCliCommand),
        typeof(UserRoleAddCliCommand),
        typeof(UserRoleRemoveCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UserRoleCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
