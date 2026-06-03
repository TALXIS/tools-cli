using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Reads background system jobs from the <c>asyncoperation</c> table.
/// </summary>
/// <example>
///   txc environment log async-jobs --errors-only --since 24h
///   txc env log async-jobs --entity account --format json
/// </example>
[CliReadOnly]
[CliCommand(
    Name = "async-jobs",
    Description = "Read background system jobs (asyncoperation) from the LIVE environment. Requires an active profile."
)]
public class LogAsyncJobsCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LogAsyncJobsCliCommand));

    [CliOption(Name = "--since", Description = "Relative time window, e.g. 30m, 24h, 7d, 2w.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--entity", Description = "Filter by the job's regarding-object entity logical name (best-effort, client-side).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--errors-only", Description = "Only failed or cancelled jobs.", Required = false)]
    public bool ErrorsOnly { get; set; }

    [CliOption(Name = "--correlation-id", Description = "Restrict to a single operation's correlation id (GUID).", Required = false)]
    public string? CorrelationId { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of jobs to return.", Required = false)]
    public int? Top { get; set; }

    [CliOption(Name = "--follow", Description = "Live tail: keep polling and print new jobs as they appear (interactive only).", Required = false)]
    public bool Follow { get; set; }

    [CliOption(Name = "--interval", Description = "Polling interval in seconds for --follow (default 5, min 2).", Required = false)]
    public string? Interval { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!EnvLogFilterBuilder.TryBuild(Since, Entity, plugin: null, ErrorsOnly, CorrelationId, Top, out var filter, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        if (Follow)
            return await RunFollowAsync(filter).ConfigureAwait(false);

        var service = TxcServices.Get<IEnvironmentLogService>();
        var rows = await service.GetAsyncJobsAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, r => PrintTable(r, "No async jobs found."));
        return ExitSuccess;
    }

    private async Task<int> RunFollowAsync(EnvironmentLogFilter filter)
    {
        if (!FollowSupport.TryParseInterval(Interval, out var interval, out var intervalError))
        {
            Logger.LogError("{Error}", intervalError);
            return ExitValidationError;
        }

        var detector = TxcServices.Get<IHeadlessDetector>();
        var isMcp = string.Equals(
            System.Environment.GetEnvironmentVariable("TXC_ENTRY_POINT"), "mcp", StringComparison.OrdinalIgnoreCase);
        if (detector.IsHeadless || isMcp)
        {
            Logger.LogError(
                "--follow is interactive-only and is not supported in non-interactive mode ({Reason}). Run a one-shot query instead.",
                isMcp ? "mcp" : detector.Reason);
            return ExitValidationError;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        System.Console.CancelKeyPress += onCancel;

        try
        {
            var service = TxcServices.Get<IEnvironmentLogService>();
            // Resolve + connect ONCE; the poll loop reuses the open connection (no per-tick log spam).
            using var reader = await service.CreateAsyncJobReaderAsync(Profile, operationTypeFilter: null, cts.Token)
                .ConfigureAwait(false);

            Logger.LogInformation(
                "Watching async jobs every {Seconds}s. Press Ctrl+C to stop.", (int)interval.TotalSeconds);

            var tracker = new FollowTracker<AsyncJobRecord>(j => j.Id.ToString());
            var headerPrinted = false;

            while (!cts.IsCancellationRequested)
            {
                IReadOnlyList<AsyncJobRecord> jobs;
                try
                {
                    jobs = await reader.ReadAsync(filter, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                foreach (var job in tracker.SelectNew(jobs))
                    EmitFollowRow(job, ref headerPrinted);

                try
                {
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled during initial connect — clean exit
        }
        finally
        {
            System.Console.CancelKeyPress -= onCancel;
        }

        return ExitSuccess;
    }

    /// <summary>
    /// Shared async-job table renderer, reused by <see cref="LogWorkflowCliCommand"/>.
    /// </summary>
    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    internal static void PrintTable(IReadOnlyList<AsyncJobRecord> rows, string emptyMessage)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine(emptyMessage);
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => (r.Name ?? "").Length), 16, 44);
        int typeWidth = Math.Clamp(rows.Max(r => (r.OperationTypeLabel ?? "").Length), 8, 24);
        int statusWidth = Math.Clamp(rows.Max(r => (r.StatusLabel ?? "").Length), 8, 18);

        string header = $"{"Created (UTC)",-19} | {"Lvl",-3} | {"Name".PadRight(nameWidth)} | {"Type".PadRight(typeWidth)} | {"Status".PadRight(statusWidth)} | Message";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string created = r.CreatedOnUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
            string level = r.IsError ? "ERR" : "ok";
            OutputWriter.WriteLine(
                $"{created,-19} | {level,-3} | {LogText.Fit(r.Name, nameWidth)} | {LogText.Fit(r.OperationTypeLabel, typeWidth)} | {LogText.Fit(r.StatusLabel, statusWidth)} | {LogText.Truncate(r.Message, 80)}");
        }
        OutputWriter.WriteLine($"({rows.Count} job{(rows.Count == 1 ? "" : "s")})");
    }

    // Streaming row for --follow: fixed column widths (no batch to size from). Header once.
    private const int FollowNameWidth = 40;
    private const int FollowTypeWidth = 20;
    private const int FollowStatusWidth = 14;

    private static string FollowHeader() =>
        $"{"Created (UTC)",-19} | {"Lvl",-3} | {"Name".PadRight(FollowNameWidth)} | {"Type".PadRight(FollowTypeWidth)} | {"Status".PadRight(FollowStatusWidth)} | Message";

    private static void EmitFollowRow(AsyncJobRecord r, ref bool headerPrinted)
    {
        if (!headerPrinted)
        {
            OutputWriter.WriteLine(FollowHeader());
            OutputWriter.WriteLine(new string('-', FollowHeader().Length));
            headerPrinted = true;
        }

        string created = r.CreatedOnUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
        string level = r.IsError ? "ERR" : "ok";
        OutputWriter.WriteLine(
            $"{created,-19} | {level,-3} | {LogText.Fit(r.Name, FollowNameWidth)} | {LogText.Fit(r.OperationTypeLabel, FollowTypeWidth)} | {LogText.Fit(r.StatusLabel, FollowStatusWidth)} | {LogText.Truncate(r.Message, 80)}");
    }
#pragma warning restore TXC003
}
