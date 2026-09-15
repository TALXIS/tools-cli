using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Enables or disables a Dataverse service principal.
/// Usage: <c>txc environment service-principal update --service-principal &lt;client-id-or-guid&gt; [--enable|--disable]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Enable or disable a Dataverse service principal. Specify exactly one of --enable or --disable."
)]
public class ServicePrincipalUpdateCliCommand : ProfiledCliCommand
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
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            await service.UpdateEnabledStateAsync(Profile, ServicePrincipal, enabled, CancellationToken.None).ConfigureAwait(false);

            var updated = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            var payload = new
            {
                status = enabled ? "enabled" : "disabled",
                servicePrincipal = updated,
            };

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
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
