using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Removes a security role from a Dataverse service principal.
/// Usage: <c>txc environment service-principal role remove --service-principal &lt;client-id-or-guid&gt; --role &lt;name-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently removes the security role assignment from the Dataverse service principal.")]
[CliCommand(
    Name = "remove",
    Description = "Remove a security role from a Dataverse service principal."
)]
public class ServicePrincipalRoleRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalRoleRemoveCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--role", Description = "Role name or GUID.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteRemoveRoleAsync();

    private async Task<int> ExecuteRemoveRoleAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            await service.RemoveRoleAsync(Profile, ServicePrincipal, Role, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "role-removed",
                servicePrincipal = ServicePrincipal,
                role = Role,
            };

            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Role '{Role}' removed from service principal '{ServicePrincipal}'.");
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
