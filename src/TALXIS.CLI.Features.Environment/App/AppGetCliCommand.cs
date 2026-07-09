using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Gets one Dataverse application user by system-user GUID or application client ID.
/// Usage: <c>txc environment app get --app &lt;client-id-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Dataverse application user by system-user GUID or application client ID."
)]
public class AppGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppGetCliCommand));

    [CliOption(Name = "--app", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string App { get; set; } = null!;

    protected override Task<int> ExecuteAsync() => ExecuteGetAsync();

    private async Task<int> ExecuteGetAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();
            var app = await service.GetAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);
            if (app is null)
            {
                Logger.LogError("Application user '{App}' not found.", App);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(app, AppCommandSupport.WriteAppDetails);
            return ExitSuccess;
        }
        catch (Exception ex) when (AppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
