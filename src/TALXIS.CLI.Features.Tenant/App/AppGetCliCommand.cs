using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Tenant.App;

/// <summary>
/// Shows one Entra application by client ID, object ID, or exact display name.
/// Usage: <c>txc tenant app get --app &lt;client-id-or-object-id&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Entra application by client ID, object ID, or exact display name."
)]
public class AppGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppGetCliCommand));

    [CliOption(Name = "--app", Description = "Application client ID, service principal object ID, or exact display name.", Required = true)]
    public string App { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteGetAsync();

    private async Task<int> ExecuteGetAsync()
    {
        try
        {
            var app = await TenantAppCommandSupport.GetAppAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteData(app, TenantAppCommandSupport.WriteAppDetail);
            return ExitSuccess;
        }
        catch (Exception ex) when (TenantAppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
