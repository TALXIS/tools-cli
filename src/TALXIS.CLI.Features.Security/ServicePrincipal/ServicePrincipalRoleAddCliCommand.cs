using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a tenant admin role when no environment is resolved. When --environment is provided or the active connection already targets an environment, assign a Dataverse security role in that environment instead."
)]
public class ServicePrincipalRoleAddCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleAddCliCommand));

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name. With an environment scope, a Dataverse system-user GUID is also accepted.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or GUID. With an environment scope, pass a Dataverse security role name or GUID instead.", Required = true)]
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
            await ServicePrincipalCommandSupport.AddTenantAssignmentAsync(Profile, ServicePrincipal, Role, CancellationToken.None).ConfigureAwait(false);
            var payload = new { status = "role-added", servicePrincipal = ServicePrincipal, role = Role };
            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to service principal '{ServicePrincipal}'.");
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
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var existingRoles = await service.ListRolesAsync(Profile, ServicePrincipal, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            if (existingRoles.Any(r => SecurityPrincipalCommandSupport.IsRoleMatch(r, Role)))
            {
                ServicePrincipalCommandSupport.WriteMutationResult(
                    new { status = "unchanged", servicePrincipal = ServicePrincipal, role = Role, environmentId = scope.EnvironmentId },
                    () =>
                    {
#pragma warning disable TXC003
                        OutputWriter.WriteLine($"Role '{Role}' is already assigned to service principal '{ServicePrincipal}'.");
#pragma warning restore TXC003
                    });
                return ExitSuccess;
            }

            await service.AddRoleAsync(Profile, ServicePrincipal, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            var payload = new { status = "role-added", servicePrincipal = ServicePrincipal, role = Role, environmentId = scope.EnvironmentId };
            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to service principal '{ServicePrincipal}'.");
#pragma warning restore TXC003
            });
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
