using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Assigns a security role to a Dataverse service principal.
/// Usage: <c>txc environment service-principal role add --service-principal &lt;client-id-or-guid&gt; --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Assign a security role to a Dataverse service principal."
)]
public class ServicePrincipalRoleAddCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleAddCliCommand));

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Role name or GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteAddRoleAsync();

    private async Task<int> ExecuteAddRoleAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();

            var existingRoles = await service.ListRolesAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            if (existingRoles.Any(r => EnvironmentPrincipalCommandSupport.IsRoleMatch(r, Role)))
            {
                ServicePrincipalCommandSupport.WriteMutationResult(
                    new { status = "unchanged", servicePrincipal = ServicePrincipal, role = Role },
                    () =>
                    {
#pragma warning disable TXC003
                        OutputWriter.WriteLine($"Role '{Role}' is already assigned to service principal '{ServicePrincipal}'.");
#pragma warning restore TXC003
                    });
                return ExitSuccess;
            }

            await service.AddRoleAsync(Profile, ServicePrincipal, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-added",
                servicePrincipal = ServicePrincipal,
                role = Role,
            };

            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' assigned to service principal '{ServicePrincipal}'.");
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
