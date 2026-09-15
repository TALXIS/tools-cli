using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Assigns a Dataverse security role to an environment user.
/// Usage: <c>txc environment user role add --user &lt;upn-or-guid&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a security role to an environment user."
)]
#pragma warning disable TXC003
public class UserRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleAddCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or system user GUID.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Security role name or role GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
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
        if (existingRoles.Any(r => EnvironmentPrincipalCommandSupport.IsRoleMatch(r, Role)))
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
                _ => OutputWriter.WriteLine($"Role '{role.Name}' is already assigned to user '{UserCliCommandSupport.FormatUserLabel(user)}'."));
            return ExitSuccess;
        }

        try
        {
            await userService.AddRoleAsync(Profile, User, Role, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (UserCliCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }

        OutputFormatter.WriteData(
            new
            {
                status = "assigned",
                userId = user.Id,
                user = UserCliCommandSupport.FormatUserLabel(user),
                roleId = role.Id,
                role = role.Name,
            },
            _ => OutputWriter.WriteLine($"Role '{role.Name}' assigned to user '{UserCliCommandSupport.FormatUserLabel(user)}'."));

        return ExitSuccess;
    }
}
