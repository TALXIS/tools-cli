using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

/// <summary>
/// Lists role assignments for a service principal. Without an environment scope this shows tenant admin roles only; with an environment scope it shows tenant admin roles and Dataverse environment security roles in separate sections.
/// Usage: <c>txc security service-principal role list --service-principal &lt;client-id-or-object-id&gt; [--environment &lt;id&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List the service principal's tenant admin roles when no environment is resolved. When --environment is provided or the active connection already targets an environment, also list the Dataverse security roles assigned in that environment under a separate labeled section."
)]
public class ServicePrincipalRoleListCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleListCliCommand));

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name. With an environment scope, a Dataverse system-user GUID is also accepted.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        var tenantAssignments = await ServicePrincipalCommandSupport.ListTenantAssignmentsAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
        if (!scope.HasEnvironment)
        {
            OutputFormatter.WriteList(tenantAssignments, SecurityPrincipalCommandSupport.WriteRoleTable);
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseServicePrincipalService>();
        var environmentAssignments = await service.ListRolesAsync(Profile, ServicePrincipal, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
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
                ServicePrincipalCommandSupport.WriteEnvironmentRoleTable));
        return ExitSuccess;
    }
}
