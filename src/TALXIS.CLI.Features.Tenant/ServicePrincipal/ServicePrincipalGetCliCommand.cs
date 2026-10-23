using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.ServicePrincipal;

/// <summary>
/// Shows one Entra application by client ID, object ID, or exact display name.
/// Usage: <c>txc tenant service-principal get --service-principal &lt;client-id-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Entra application by client ID, object ID, or exact display name."
)]
public class ServicePrincipalGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalGetCliCommand));

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteGetAsync();

    private async Task<int> ExecuteGetAsync()
    {
        try
        {
            var app = await TenantServicePrincipalCommandSupport.GetServicePrincipalAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteData(app, TenantServicePrincipalCommandSupport.WriteServicePrincipalDetail);
            return ExitSuccess;
        }
        catch (Exception ex) when (TenantPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
