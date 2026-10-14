using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

[CliDestructive("Removes the selected role assignment. With an environment scope this removes a Dataverse security role; otherwise it removes a tenant admin role.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a tenant admin role when no environment is resolved. When --environment is provided or the active connection already targets an environment, remove a Dataverse security role from that environment instead."
)]
public class UserRoleRemoveCliCommand : SecurityScopedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--user", Description = "User principal name or Entra object ID. With an environment scope, a Dataverse system user GUID is also accepted.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or role ID. With an environment scope, pass a Dataverse security role name or GUID instead.", Required = true)]
    public string Role { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        return scope.HasEnvironment
            ? await ExecuteEnvironmentRemoveAsync(scope).ConfigureAwait(false)
            : await ExecuteTenantRemoveAsync().ConfigureAwait(false);
    }

    private async Task<int> ExecuteTenantRemoveAsync()
    {
        try
        {
            await UserCommandSupport.RemoveTenantRoleAsync(Profile, User, Role, CancellationToken.None).ConfigureAwait(false);
            var payload = new { status = "role-removed", user = User, role = Role };
            SecurityPrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from user '{User}'.");
#pragma warning restore TXC003
            });
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task<int> ExecuteEnvironmentRemoveAsync(SecurityScopeContext scope)
    {
        var userService = TxcServices.Get<IDataverseUserService>();
        var roleService = TxcServices.Get<IDataverseRoleService>();

        var user = await UserCommandSupport.ResolveEnvironmentUserAsync(userService, Profile, User, scope.EnvironmentId, Logger, CancellationToken.None).ConfigureAwait(false);
        if (user is null)
            return ExitValidationError;

        var role = await UserCommandSupport.ResolveEnvironmentRoleAsync(roleService, Profile, Role, scope.EnvironmentId, Logger, CancellationToken.None).ConfigureAwait(false);
        if (role is null)
            return ExitValidationError;

        var existingRoles = await userService.ListRolesAsync(Profile, User, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
        if (!existingRoles.Any(r => r.Id == role.Id))
        {
            OutputFormatter.WriteData(
                new { status = "unchanged", userId = user.Id, user = UserCommandSupport.FormatUserLabel(user), roleId = role.Id, role = role.Name, environmentId = scope.EnvironmentId },
                _ => OutputWriter.WriteLine($"Role '{role.Name}' is not assigned to user '{UserCommandSupport.FormatUserLabel(user)}'."));
            return ExitSuccess;
        }

        try
        {
            await userService.RemoveRoleAsync(Profile, User, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }

        OutputFormatter.WriteData(
            new { status = "removed", userId = user.Id, user = UserCommandSupport.FormatUserLabel(user), roleId = role.Id, role = role.Name, environmentId = scope.EnvironmentId },
            _ => OutputWriter.WriteLine($"Role '{role.Name}' removed from user '{UserCommandSupport.FormatUserLabel(user)}'."));
        return ExitSuccess;
    }
}
