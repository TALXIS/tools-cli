using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Parent command for tenant-wide Entra user discovery and Dataverse environment-user access management.
/// Usage: <c>txc security user [list|get|add|update|role|self-elevate]</c>
/// </summary>
[CliCommand(
    Name = "user",
    Description = "Discover Entra users tenant-wide, or manage Dataverse environment users when --environment is provided or resolved from the active connection.",
    Children = new[]
    {
        typeof(UserListCliCommand),
        typeof(UserGetCliCommand),
        typeof(UserAddCliCommand),
        typeof(UserUpdateCliCommand),
        typeof(UserRoleCliCommand),
        typeof(UserSelfElevateCliCommand)
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
/// Sub-resource for tenant-wide and Dataverse security-role assignments on a user.
/// Usage: <c>txc security user role [list|add|remove]</c>
/// </summary>
[CliCommand(
    Name = "role",
    Description = "List, add, or remove tenant admin roles and Dataverse security roles for a user. With an environment scope, role list shows tenant admin roles and environment security roles in separate sections.",
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
