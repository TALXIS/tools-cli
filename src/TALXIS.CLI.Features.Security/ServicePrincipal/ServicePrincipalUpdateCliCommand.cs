using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

/// <summary>
/// Enables or disables a Dataverse service principal.
/// Usage: <c>txc security service-principal update --service-principal &lt;client-id-or-guid&gt; [--enable|--disable] [--environment &lt;id&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Enable or disable a Dataverse service principal. This command requires --environment or an active environment connection because there is no tenant-wide service-principal state mutation equivalent. Specify exactly one of --enable or --disable."
)]
public class ServicePrincipalUpdateCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalUpdateCliCommand));

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    [CliOption(Name = "--enable", Description = "Enable the service principal.", Required = false)]
    public bool Enable { get; set; }

    [CliOption(Name = "--disable", Description = "Disable the service principal.", Required = false)]
    public bool Disable { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!ServicePrincipalCommandSupport.TryResolveEnabledState(Enable, Disable, Logger, out var enabled))
            return Task.FromResult(ExitValidationError);

        return ExecuteUpdateAsync(enabled);
    }

    private async Task<int> ExecuteUpdateAsync(bool enabled)
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveRequiredEnvironmentScopeAsync(Profile, Environment, "txc security service-principal update", CancellationToken.None).ConfigureAwait(false);

        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            await service.UpdateEnabledStateAsync(Profile, ServicePrincipal, enabled, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);

            var updated = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            var payload = new { status = enabled ? "enabled" : "disabled", environmentId = scope.EnvironmentId, servicePrincipal = updated };

            ServicePrincipalCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine($"Service principal {(enabled ? "enabled" : "disabled")}.");
                if (updated is not null)
                    ServicePrincipalCommandSupport.WriteServicePrincipalDetails(updated);
                else
                    OutputWriter.WriteLine($"Identifier: {ServicePrincipal}");
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
