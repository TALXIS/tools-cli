using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// <c>txc environment delete</c> — permanently deletes a Power Platform
/// environment from the tenant. This is an irreversible, tenant-level admin
/// operation: the active profile supplies the credential and cloud. The BAP
/// admin API validates that the environment can be deleted before initiating
/// the operation. By default the command returns after queueing; pass
/// <c>--wait</c> to block until deletion completes.
/// </summary>
[CliDestructive("Permanently deletes a Power Platform environment and all its data. This action is irreversible.")]
[CliLongRunning]
[CliCommand(
    Name = "delete",
    Description = "Permanently delete a Power Platform environment from the tenant. Requires an active profile (used for admin identity and cloud). This action is irreversible."
)]
public class EnvironmentDeleteCliCommand : ProfiledCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentDeleteCliCommand));

    [CliArgument(Name = "id", Description = "Environment id (GUID) of the environment to delete.")]
    public Guid EnvironmentId { get; set; }

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--wait", Description = "Wait for deletion to complete. By default the command returns after queueing.", Required = false)]
    public bool Wait { get; set; }

    [CliOption(Name = "--max-wait-minutes", Description = "Maximum minutes to wait when --wait is set (default 60).", Required = false)]
    public int MaxWaitMinutes { get; set; } = 60;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IEnvironmentManagementService>();
        var result = await service.DeleteAsync(
            Profile,
            EnvironmentId,
            Wait,
            TimeSpan.FromMinutes(Math.Max(1, MaxWaitMinutes)),
            CancellationToken.None).ConfigureAwait(false);

        if (result.Completed)
            Logger.LogInformation("Environment {EnvironmentId} deleted.", result.EnvironmentId);
        else
            Logger.LogInformation("Environment deletion queued ({EnvironmentId}); status {Status}.", result.EnvironmentId, result.Status);

        var payload = new
        {
            environmentId = result.EnvironmentId,
            status = result.Status,
            completed = result.Completed,
            operationLocation = result.OperationLocation?.ToString(),
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine($"Environment ID: {result.EnvironmentId}");
            OutputWriter.WriteLine($"Status: {result.Status}");
            if (!result.Completed)
                OutputWriter.WriteLine("Deletion is in progress. Pass --wait next time to block until complete.");
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }
}
