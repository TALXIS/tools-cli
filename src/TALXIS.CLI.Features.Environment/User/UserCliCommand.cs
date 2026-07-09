using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Parent command for environment-user operations.
/// Usage: <c>txc environment user [list|get|update|role|self-elevate]</c>
/// </summary>
[CliCommand(
    Name = "user",
    Description = "Manage Dataverse environment users and their security roles.",
    Children = new[]
    {
        typeof(UserListCliCommand),
        typeof(UserGetCliCommand),
        typeof(UserAddCliCommand),
        typeof(UserUpdateCliCommand),
        typeof(UserRoleCliCommand),
        typeof(UserSelfElevateCliCommand),
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
/// Sub-resource for Dataverse security-role assignments on environment users.
/// Usage: <c>txc environment user role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "List, add, or remove security roles for an environment user.",
    Children = new[]
    {
        typeof(UserRoleListCliCommand),
        typeof(UserRoleAddCliCommand),
        typeof(UserRoleRemoveCliCommand),
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
