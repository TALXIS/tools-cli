using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a tenant admin role when no environment is resolved. When --environment is provided or the active connection already targets an environment, assign a Dataverse security role in that environment instead."
)]
public class UserRoleAddCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleAddCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object ID. With an environment scope, a Dataverse system user GUID is also accepted.", Required = true)]
    public string User { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or role ID. With an environment scope, pass a Dataverse security role name or GUID instead.", Required = true)]
    public string Role { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        return scope.HasEnvironment
            ? await ExecuteEnvironmentAddAsync(scope).ConfigureAwait(false)
            : await ExecuteTenantAddAsync().ConfigureAwait(false);
    }

    private async Task<int> ExecuteTenantAddAsync()
    {
        try
        {
            await UserCommandSupport.AddTenantRoleAsync(Profile, User, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new { status = "role-added", user = User, role = Role };
            SecurityPrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to user '{User}'.");
#pragma warning restore TXC003
            });
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task<int> ExecuteEnvironmentAddAsync(SecurityScopeContext scope)
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
        if (existingRoles.Any(r => SecurityPrincipalCommandSupport.IsRoleMatch(r, Role)))
        {
            OutputFormatter.WriteData(
                new { status = "unchanged", userId = user.Id, user = UserCommandSupport.FormatUserLabel(user), roleId = role.Id, role = role.Name, environmentId = scope.EnvironmentId },
                _ => OutputWriter.WriteLine($"Role '{role.Name}' is already assigned to user '{UserCommandSupport.FormatUserLabel(user)}'."));
            return ExitSuccess;
        }

        try
        {
            await userService.AddRoleAsync(Profile, User, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }

        OutputFormatter.WriteData(
            new { status = "assigned", userId = user.Id, user = UserCommandSupport.FormatUserLabel(user), roleId = role.Id, role = role.Name, environmentId = scope.EnvironmentId },
            _ => OutputWriter.WriteLine($"Role '{role.Name}' assigned to user '{UserCommandSupport.FormatUserLabel(user)}'."));
        return ExitSuccess;
    }
}
