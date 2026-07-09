using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Removes a Dataverse security role from an environment user.
/// Usage: <c>txc environment user role remove --user &lt;upn-or-guid&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliDestructive("Removing a security role can immediately reduce what the user can do in the environment.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a security role from an environment user."
)]
#pragma warning disable TXC003
public class UserRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--user", Description = "User principal name or system user GUID.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Security role name or role GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        var userService = TxcServices.Get<IDataverseUserService>();
        var roleService = TxcServices.Get<IDataverseRoleService>();

        var user = await UserCliCommandSupport.ResolveUserAsync(
            userService,
            Profile,
            User,
            Logger,
            CancellationToken.None).ConfigureAwait(false);
        if (user is null)
            return ExitValidationError;

        var role = await UserCliCommandSupport.ResolveRoleAsync(
            roleService,
            Profile,
            Role,
            Logger,
            CancellationToken.None).ConfigureAwait(false);
        if (role is null)
            return ExitValidationError;

        var existingRoles = await userService.ListRolesAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);
        if (!existingRoles.Any(r => r.Id == role.Id))
        {
            OutputFormatter.WriteData(
                new
                {
                    status = "unchanged",
                    userId = user.Id,
                    user = UserCliCommandSupport.FormatUserLabel(user),
                    roleId = role.Id,
                    role = role.Name,
                },
                _ => OutputWriter.WriteLine($"Role '{role.Name}' is not assigned to user '{UserCliCommandSupport.FormatUserLabel(user)}'."));
            return ExitSuccess;
        }

        try
        {
            await userService.RemoveRoleAsync(Profile, User, Role, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (UserCliCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }

        OutputFormatter.WriteData(
            new
            {
                status = "removed",
                userId = user.Id,
                user = UserCliCommandSupport.FormatUserLabel(user),
                roleId = role.Id,
                role = role.Name,
            },
            _ => OutputWriter.WriteLine($"Role '{role.Name}' removed from user '{UserCliCommandSupport.FormatUserLabel(user)}'."));

        return ExitSuccess;
    }
}
