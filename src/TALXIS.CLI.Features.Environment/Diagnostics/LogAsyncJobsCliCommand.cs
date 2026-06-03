using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
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

    protected override async Task<int> ExecuteAsync()
    {
        if (!EnvLogFilterBuilder.TryBuild(Since, Entity, plugin: null, ErrorsOnly, CorrelationId, Top, out var filter, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IEnvironmentLogService>();
        var rows = await service.GetAsyncJobsAsync(Profile, filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, r => PrintTable(r, "No async jobs found."));
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
#pragma warning restore TXC003
}
