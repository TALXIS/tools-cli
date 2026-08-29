using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

[CliDestructive("Removes the selected role assignment. With an environment scope this removes a Dataverse security role; otherwise it removes a tenant admin role.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a tenant admin role when no environment is resolved. When --environment is provided or the active connection already targets an environment, remove a Dataverse security role from that environment instead."
)]
public class ServicePrincipalRoleRemoveCliCommand : SecurityScopedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name. With an environment scope, a Dataverse system-user GUID is also accepted.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or GUID. With an environment scope, pass a Dataverse security role name or GUID instead.", Required = true)]
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
            await ServicePrincipalCommandSupport.RemoveTenantAssignmentAsync(Profile, ServicePrincipal, Role, CancellationToken.None).ConfigureAwait(false);
            var payload = new { status = "role-removed", servicePrincipal = ServicePrincipal, role = Role };
            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from service principal '{ServicePrincipal}'.");
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
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            await service.RemoveRoleAsync(Profile, ServicePrincipal, Role, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            var payload = new { status = "role-removed", servicePrincipal = ServicePrincipal, role = Role, environmentId = scope.EnvironmentId };
            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from service principal '{ServicePrincipal}'.");
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
