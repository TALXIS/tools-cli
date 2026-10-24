using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.User;

/// <summary>
/// Lists role assignments for a user. Without an environment scope this shows tenant admin roles only; with an environment scope it shows tenant admin roles and Dataverse environment security roles in separate sections.
/// Usage: <c>txc security user role list --user &lt;upn-or-object-id&gt; [--environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List the user's tenant admin roles when no environment is resolved. When --environment is provided or the active connection already targets an environment, also list the user's Dataverse security roles for that environment under a separate labeled section."
)]
public class UserRoleListCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserRoleListCliCommand));

    [CliOption(Name = "--user", Description = "User principal name or Entra object ID. With an environment scope, a Dataverse system user GUID is also accepted.", Required = true)]
    public string User { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        var tenantAssignments = await UserCommandSupport.ListTenantRolesAsync(Profile, User, CancellationToken.None).ConfigureAwait(false);
        if (!scope.HasEnvironment)
        {
            OutputFormatter.WriteList(tenantAssignments, SecurityPrincipalCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }

        var userService = TxcServices.Get<IDataverseUserService>();
        var environmentAssignments = await userService.ListRolesAsync(Profile, User, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
        var payload = new
        {
            tenantAdminRoles = tenantAssignments,
            environmentId = scope.EnvironmentId,
            environmentDisplayName = scope.EnvironmentDisplayName,
            environmentSecurityRoles = environmentAssignments,
        };

        OutputFormatter.WriteData(payload, _ =>
            SecurityPrincipalCommandSupport.WriteCombinedRoleSections(
                tenantAssignments,
                environmentAssignments,
                scope,
                UserCommandSupport.PrintEnvironmentRolesTable));
        return ExitSuccess;
    }
}
