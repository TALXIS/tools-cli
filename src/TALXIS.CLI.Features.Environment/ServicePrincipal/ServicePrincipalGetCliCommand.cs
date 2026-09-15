using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Gets one Dataverse service principal by system-user GUID or application client ID.
/// Usage: <c>txc environment service-principal get --service-principal &lt;client-id-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Dataverse service principal by system-user GUID or application client ID."
)]
public class ServicePrincipalGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalGetCliCommand));

    [CliOption(Name = "--service-principal", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteGetAsync();

    private async Task<int> ExecuteGetAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var app = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            if (app is null)
            {
                Logger.LogError("Service principal '{ServicePrincipal}' not found.", ServicePrincipal);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(app, ServicePrincipalCommandSupport.WriteServicePrincipalDetails);
            return ExitSuccess;
        }
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
