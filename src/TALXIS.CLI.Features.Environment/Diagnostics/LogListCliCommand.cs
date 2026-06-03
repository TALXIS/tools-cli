using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Unified recent-log feed that merges plug-in traces and async jobs into a single
/// time-ordered stream.
/// </summary>
/// <example>
///   txc environment log list --since 24h
///   txc env log list --errors-only --format json
/// </example>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Unified recent log feed across sources (plug-in traces + async jobs) from the LIVE environment."
)]
public class LogListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(LogListCliCommand));

    [CliOption(Name = "--since", Description = "Relative time window, e.g. 30m, 24h, 7d, 2w.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--entity", Description = "Filter by entity logical name (plug-in primary entity / async regarding object).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--plugin", Description = "Filter plug-in traces by type-name (substring match).", Required = false)]
    public string? Plugin { get; set; }

    [CliOption(Name = "--errors-only", Description = "Only error rows (traces with an exception, failed/cancelled jobs).", Required = false)]
    public bool ErrorsOnly { get; set; }

    [CliOption(Name = "--correlation-id", Description = "Restrict to a single operation's correlation id (GUID).", Required = false)]
    public string? CorrelationId { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of rows to fetch per source before merging.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!EnvLogFilterBuilder.TryBuild(Since, Entity, Plugin, ErrorsOnly, CorrelationId, Top, out var filter, out var error))
        {
            Logger.LogError("{Error}", error);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IEnvironmentLogService>();
        var tracesTask = service.GetPluginTracesAsync(Profile, filter, CancellationToken.None);
        var jobsTask = service.GetAsyncJobsAsync(Profile, filter, CancellationToken.None);
        await Task.WhenAll(tracesTask, jobsTask).ConfigureAwait(false);

        var rows = BuildRows(await tracesTask.ConfigureAwait(false), await jobsTask.ConfigureAwait(false), filter.ErrorsOnly);

        // Without an explicit window, show a compact recent slice; with --since, show everything in range.
        int max = filter.SinceUtc is null ? 20 : rows.Count;
        var trimmed = rows.Take(max).ToList();

        OutputFormatter.WriteList(trimmed, PrintTable);
        return ExitSuccess;
    }

    /// <summary>
    /// Normalizes plug-in traces and async jobs into a single feed sorted by
    /// timestamp descending. When <paramref name="errorsOnly"/> is set, keeps only
    /// rows whose level is <c>ERROR</c>.
    /// </summary>
    public static IReadOnlyList<EnvLogRow> BuildRows(
        IReadOnlyList<PluginTraceRecord> traces,
        IReadOnlyList<AsyncJobRecord> jobs,
        bool errorsOnly)
    {
        var rows = new List<EnvLogRow>(traces.Count + jobs.Count);

        foreach (var t in traces)
        {
            rows.Add(new EnvLogRow(
                Source: "plugin",
                TimestampUtc: t.CreatedOnUtc,
                Level: t.HasException ? "ERROR" : "OK",
                Name: t.TypeName ?? "(unknown)",
                Entity: t.PrimaryEntity,
                CorrelationId: t.CorrelationId,
                Message: t.HasException ? t.ExceptionSnippet : t.MessageSnippet));
        }

        foreach (var j in jobs)
        {
            rows.Add(new EnvLogRow(
                Source: "async",
                TimestampUtc: j.CreatedOnUtc,
                Level: j.IsError ? "ERROR" : "OK",
                Name: j.Name ?? j.OperationTypeLabel ?? "(unknown)",
                Entity: j.RegardingEntity,
                CorrelationId: j.CorrelationId,
                Message: j.Message));
        }

        IEnumerable<EnvLogRow> result = rows;
        if (errorsOnly)
            result = result.Where(r => r.Level == "ERROR");

        return result
            .OrderByDescending(r => r.TimestampUtc ?? DateTime.MinValue)
            .ToList();
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<EnvLogRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No log entries found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 16, 44);
        int entityWidth = Math.Clamp(rows.Max(r => (r.Entity ?? "").Length), 6, 24);

        string header = $"{"Created (UTC)",-19} | {"Src",-6} | {"Lvl",-5} | {"Name".PadRight(nameWidth)} | {"Entity".PadRight(entityWidth)} | Message";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string created = r.TimestampUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
            OutputWriter.WriteLine(
                $"{created,-19} | {r.Source,-6} | {r.Level,-5} | {LogText.Fit(r.Name, nameWidth)} | {LogText.Fit(r.Entity, entityWidth)} | {LogText.Truncate(r.Message, 70)}");
        }
        OutputWriter.WriteLine($"({rows.Count} entr{(rows.Count == 1 ? "y" : "ies")})");
    }
#pragma warning restore TXC003
}

/// <summary>
/// Unified row shape emitted by <c>txc environment log list</c>. Same contract in
/// text and JSON.
/// </summary>
public sealed record EnvLogRow(
    string Source,
    DateTime? TimestampUtc,
    string Level,
    string Name,
    string? Entity,
    Guid? CorrelationId,
    string? Message);
