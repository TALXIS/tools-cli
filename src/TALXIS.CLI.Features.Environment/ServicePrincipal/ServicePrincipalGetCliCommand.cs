using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

/// <summary>
/// Gets one Dataverse application user by system-user GUID or application client ID.
/// Usage: <c>txc environment service-principal get --service-principal &lt;client-id-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Dataverse application user by system-user GUID or application client ID."
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
            var service = TxcServices.Get<IDataverseAppUserService>();
            var app = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            if (app is null)
            {
                Logger.LogError("Application user '{ServicePrincipal}' not found.", ServicePrincipal);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(app, ServicePrincipalCommandSupport.WriteAppDetails);
            return ExitSuccess;
        }
        catch (Exception ex) when (ServicePrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
