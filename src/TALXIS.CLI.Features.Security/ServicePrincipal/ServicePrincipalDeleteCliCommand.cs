using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

/// <summary>
/// Hard-deletes a Dataverse service principal.
/// Usage: <c>txc security service-principal delete --service-principal &lt;client-id-or-guid&gt; --yes [--environment &lt;id&gt;]</c>
/// </summary>
[CliDestructive("Permanently deletes the Dataverse service principal from the resolved environment.")]
[CliCommand(
    Name = "delete",
    Description = "Hard-delete a Dataverse service principal. This command requires --environment or an active environment connection because there is no tenant-wide delete equivalent. The record must already be disabled before Dataverse will allow the delete."
)]
public class ServicePrincipalDeleteCliCommand : SecurityScopedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteDeleteAsync();

    private async Task<int> ExecuteDeleteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security service-principal delete", CancellationToken.None).ConfigureAwait(false);

        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var existing = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            if (existing is null)
            {
                Logger.LogError("Service principal '{ServicePrincipal}' not found.", ServicePrincipal);
                return ExitValidationError;
            }

            await service.DeleteAsync(Profile, ServicePrincipal, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            var payload = new { status = "deleted", environmentId = scope.EnvironmentId, servicePrincipal = existing };

            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine("Service principal deleted.");
                ServicePrincipalCommandSupport.WriteServicePrincipalDetails(existing);
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
