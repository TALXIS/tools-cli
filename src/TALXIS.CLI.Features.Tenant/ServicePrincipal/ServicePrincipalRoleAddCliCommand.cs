using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.ServicePrincipal;

/// <summary>
/// Assigns a tenant-wide role to an Entra application.
/// Usage: <c>txc tenant service-principal role add --service-principal &lt;client-id-or-object-id&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a tenant-wide role to an Entra application."
)]
public class ServicePrincipalRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleAddCliCommand));

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Tenant role name or GUID. Use 'admin-application' to allow this service principal to call txc environment admin commands non-interactively.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        try
        {
            await TenantServicePrincipalCommandSupport.AddAssignmentAsync(Profile, ServicePrincipal, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                servicePrincipal = ServicePrincipal,
                role = Role,
            };

            TenantServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to service principal '{ServicePrincipal}'.");
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
