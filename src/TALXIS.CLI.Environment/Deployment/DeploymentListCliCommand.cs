using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Environment.Deployment;

[CliCommand(
    Name = "list",
    Description = "List past deployment runs (package and solution) in the target environment."
)]
public class DeploymentListCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeploymentListCliCommand));

    [CliOption(Name = "--kind", Description = "Filter runs by kind: package or solution. Omit to include both.", Required = false)]
    public string? Kind { get; set; }

    [CliOption(Name = "--since", Description = "Relative time window, e.g. 30m, 24h, 7d, 2w.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--problems", Description = "Only rows that are not Success/Completed.", Required = false)]
    public bool Problems { get; set; }

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        bool includePackages = true;
        bool includeSolutions = true;
        if (!string.IsNullOrWhiteSpace(Kind))
        {
            var k = Kind.Trim().ToLowerInvariant();
            switch (k)
            {
                case "package":
                case "pkg":
                    includeSolutions = false;
                    break;
                case "solution":
                case "sol":
                    includePackages = false;
                    break;
                default:
                    _logger.LogError("Invalid --kind '{Kind}'. Expected 'package' or 'solution'.", Kind);
                    return 1;
            }
        }

        DateTime? sinceUtc = null;
        int defaultCount = 20;
        if (!string.IsNullOrWhiteSpace(Since))
        {
            if (!DeploymentRelativeTimeParser.TryParse(Since, out var window))
            {
                _logger.LogError("Invalid --since value '{Value}'. Use NNNm, NNNh, NNNd, or NNNw.", Since);
                return 1;
            }
            sinceUtc = DateTime.UtcNow - window;
            defaultCount = 200;
        }

        DeploymentHistorySnapshot snapshot;
        try
        {
            var service = TxcServices.Get<IDeploymentHistoryService>();
            snapshot = await service.GetRecentAsync(
                Profile,
                includePackages,
                includeSolutions,
                defaultCount,
                sinceUtc,
                Problems,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment deployment list failed");
            return 1;
        }

        var rows = BuildRows(snapshot.Packages, snapshot.Solutions);
        int max = sinceUtc is null ? 20 : rows.Count;
        var trimmed = rows.Take(max).ToList();

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(trimmed, JsonOptions));
            return 0;
        }

        PrintRunsTable(trimmed);
        return 0;
    }

    /// <summary>
    /// Normalizes and merges both run streams into a single list sorted by start time desc.
    /// </summary>
    public static IReadOnlyList<DeploymentListRow> BuildRows(
        IReadOnlyList<PackageHistoryRecord> packages,
        IReadOnlyList<SolutionHistoryRecord> solutions)
    {
        var rows = new List<DeploymentListRow>(packages.Count + solutions.Count);

        foreach (var p in packages)
        {
            rows.Add(new DeploymentListRow(
                Kind: "pkg",
                Id: p.Id,
                Name: p.Name,
                Status: ShortPackageStatus(p.Status),
                StartedAtUtc: p.StartedAtUtc,
                CompletedAtUtc: p.CompletedAtUtc));
        }

        foreach (var s in solutions)
        {
            rows.Add(new DeploymentListRow(
                Kind: "sol",
                Id: s.Id,
                Name: s.SolutionName,
                Status: ShortSolutionStatus(s.Result),
                StartedAtUtc: s.StartedAtUtc,
                CompletedAtUtc: s.CompletedAtUtc));
        }

        return rows
            .OrderByDescending(r => r.StartedAtUtc ?? DateTime.MinValue)
            .ToList();
    }

    private static string ShortPackageStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "UNKNOWN";
        var s = status.Trim();
        if (s.Equals("Success", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Completed", StringComparison.OrdinalIgnoreCase)) return "OK";
        if (s.Contains("Fail", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Error", StringComparison.OrdinalIgnoreCase)) return "FAILED";
        if (s.Contains("In Process", StringComparison.OrdinalIgnoreCase)
            || s.Contains("InProgress", StringComparison.OrdinalIgnoreCase)
            || s.Contains("In Progress", StringComparison.OrdinalIgnoreCase)) return "IN PROGRESS";
        return s.ToUpperInvariant();
    }

    private static string ShortSolutionStatus(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return "UNKNOWN";
        var r = result.Trim();
        if (r.Contains("success", StringComparison.OrdinalIgnoreCase)
            || r.Contains("completed", StringComparison.OrdinalIgnoreCase)) return "OK";
        if (r.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || r.Contains("error", StringComparison.OrdinalIgnoreCase)) return "FAILED";
        if (r.Contains("progress", StringComparison.OrdinalIgnoreCase)) return "IN PROGRESS";
        return r.Length > 20 ? r[..20].ToUpperInvariant() : r.ToUpperInvariant();
    }

    private static void PrintRunsTable(IReadOnlyList<DeploymentListRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No deployment runs found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => (r.Name ?? "").Length), 12, 40);
        int statusWidth = Math.Max(8, rows.Max(r => r.Status.Length));

        string header = $"Kind | {"Name".PadRight(nameWidth)} | {"Status".PadRight(statusWidth)} | {"Started (UTC)",-19} | Duration";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string name = (r.Name ?? "(unknown)");
            if (name.Length > nameWidth) name = name[..(nameWidth - 1)] + ".";
            string started = r.StartedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
            string duration = FormatDuration(r.StartedAtUtc, r.CompletedAtUtc);
            OutputWriter.WriteLine($"{r.Kind,-4} | {name.PadRight(nameWidth)} | {r.Status.PadRight(statusWidth)} | {started,-19} | {duration}");
        }
    }

    private static string FormatDuration(DateTime? start, DateTime? end)
    {
        if (start is null) return "(unknown)";
        if (end is null) return "running";
        var span = end.Value - start.Value;
        return span.TotalSeconds < 60
            ? $"{span.TotalSeconds:0.#}s"
            : $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

/// <summary>
/// Unified row shape emitted by <c>txc environment deployment list</c>.
/// Same contract in text and JSON.
/// </summary>
public sealed record DeploymentListRow(
    string Kind,
    Guid Id,
    string? Name,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);
