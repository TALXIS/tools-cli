using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.ServicePrincipal;

/// <summary>
/// Removes a tenant-wide role from an Entra application.
/// Usage: <c>txc tenant service-principal role remove --service-principal &lt;client-id-or-object-id&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Removes the tenant-wide role assignment. If --role admin-application is used, this also revokes the service principal's ability to call txc environment admin commands non-interactively.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a tenant-wide role from an Entra application."
)]
public class ServicePrincipalRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or GUID. Use 'admin-application' to revoke this service principal's ability to call txc environment admin commands non-interactively.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        try
        {
            await TenantServicePrincipalCommandSupport.RemoveAssignmentAsync(Profile, ServicePrincipal, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-removed",
                servicePrincipal = ServicePrincipal,
                role = Role,
            };

            TenantServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from service principal '{ServicePrincipal}'.");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (TenantPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
