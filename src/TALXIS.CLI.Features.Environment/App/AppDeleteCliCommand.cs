using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.App;

/// <summary>
/// Hard-deletes a Dataverse application user.
/// Usage: <c>txc environment app delete --app &lt;client-id-or-guid&gt; --yes</c>
/// </summary>
[CliDestructive("Permanently deletes the Dataverse application user from the environment.")]
[CliCommand(
    Name = "delete",
    Description = "Hard-delete a Dataverse application user. The record must already be disabled before Dataverse will allow the delete."
)]
public class AppDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(AppDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--app", Description = "System-user GUID or application client ID GUID.", Required = true)]
    public string App { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseAppUserService>();
            var existing = await service.GetAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);
            if (existing is null)
            {
                Logger.LogError("Application user '{App}' not found.", App);
                return ExitValidationError;
            }

            await service.DeleteAsync(Profile, App, CancellationToken.None).ConfigureAwait(false);

            var payload = new
            {
                status = "deleted",
                appUser = existing,
            };

            AppCommandSupport.WriteMutationResult(payload, () =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine("Application user deleted.");
                AppCommandSupport.WriteAppDetails(existing);
#pragma warning restore TXC003
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (AppCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
