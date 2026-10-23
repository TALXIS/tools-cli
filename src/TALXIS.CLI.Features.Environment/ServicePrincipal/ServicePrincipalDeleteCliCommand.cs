using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Hard-deletes a Dataverse service principal.
/// Usage: <c>txc environment service-principal delete --service-principal &lt;client-id-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently deletes the Dataverse service principal from the environment.")]
[CliCommand(
    Name = "delete",
    Description = "Hard-delete a Dataverse service principal. The record must already be disabled before Dataverse will allow the delete."
)]
public class ServicePrincipalDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteDeleteAsync();

    private async Task<int> ExecuteDeleteAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var existing = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            if (existing is null)
            {
                Logger.LogError("Service principal '{ServicePrincipal}' not found.", ServicePrincipal);
                return ExitValidationError;
            }

            await service.DeleteAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "deleted",
                servicePrincipal = existing,
            };

            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine("Service principal deleted.");
                ServicePrincipalCommandSupport.WriteServicePrincipalDetails(existing);
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
