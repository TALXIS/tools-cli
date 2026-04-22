using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Deploy;

/// <summary>
/// Lists deployment-related resources from Dataverse.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List deployment resources. Use --resource runs or --resource solutions."
)]
public class DeployListCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployListCliCommand));

    [CliOption(Name = "--resource", Description = "Resource to list: runs or solutions.", Required = true)]
    public required string Resource { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--since", Description = "Relative time window for --resource runs, e.g. 30m, 24h, 7d, 2w.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--problems", Description = "Only rows that are not Success/Completed. Only valid with --resource runs.", Required = false)]
    public bool Problems { get; set; }

    [CliOption(Name = "--managed", Description = "Filter installed solutions by managed status (true/false). Only valid with --resource solutions.", Required = false)]
    public string? Managed { get; set; }

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Resource))
        {
            _logger.LogError("--resource is required (runs|solutions).");
            return 1;
        }

        var resource = Resource.Trim().ToLowerInvariant();
        return resource switch
        {
            "runs" => await ListRunsAsync().ConfigureAwait(false),
            "solutions" => await ListSolutionsAsync().ConfigureAwait(false),
            _ => InvalidResource(),
        };
    }

    private async Task<int> ListRunsAsync()
    {
        if (!string.IsNullOrWhiteSpace(Managed))
        {
            _logger.LogError("--managed is only valid with --resource solutions.");
            return 1;
        }

        DateTime? sinceUtc = null;
        int defaultCount = 20;
        if (!string.IsNullOrWhiteSpace(Since))
        {
            if (!DeployRelativeTimeParser.TryParse(Since, out var window))
            {
                _logger.LogError("Invalid --since value '{Value}'. Use NNNm, NNNh, NNNd, or NNNw.", Since);
                return 1;
            }
            sinceUtc = DateTime.UtcNow - window;
            defaultCount = 200;
        }

        DataverseConnection conn;
        try
        {
            conn = ServiceClientFactory.Connect(ConnectionString, EnvironmentUrl, DeviceCode, Verbose, _logger);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }

        using (conn)
        {
            try
            {
                var pkgReader = new PackageHistoryReader(conn.Client, _logger);
                var solReader = new SolutionHistoryReader(conn.Client, _logger);

                var pkgTask = pkgReader.GetRecentAsync(defaultCount, sinceUtc, Problems);
                var solTask = solReader.GetRecentAsync(defaultCount, sinceUtc, Problems);
                await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);

                var rows = BuildRows(await pkgTask.ConfigureAwait(false), await solTask.ConfigureAwait(false));
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "deploy list --resource runs failed");
                return 1;
            }
        }
    }

    private async Task<int> ListSolutionsAsync()
    {
        if (!string.IsNullOrWhiteSpace(Since) || Problems)
        {
            _logger.LogError("--since and --problems are only valid with --resource runs.");
            return 1;
        }

        bool? managedFilter = null;
        if (!string.IsNullOrWhiteSpace(Managed))
        {
            if (!bool.TryParse(Managed, out var parsedManaged))
            {
                _logger.LogError("Invalid --managed value '{Value}'. Use true or false.", Managed);
                return 1;
            }
            managedFilter = parsedManaged;
        }

        DataverseConnection conn;
        try
        {
            conn = ServiceClientFactory.Connect(ConnectionString, EnvironmentUrl, DeviceCode, Verbose, _logger);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }

        using (conn)
        {
            try
            {
                var reader = new SolutionInventoryReader(conn.Client);
                var rows = await reader.ListAsync(managedFilter).ConfigureAwait(false);

                if (Json)
                {
                    OutputWriter.WriteLine(JsonSerializer.Serialize(rows, JsonOptions));
                    return 0;
                }

                PrintSolutionsTable(rows);
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "deploy list --resource solutions failed");
                return 1;
            }
        }
    }

    /// <summary>
    /// Normalizes and merges both run streams into a single list sorted by start time desc.
    /// </summary>
    public static IReadOnlyList<DeployListRow> BuildRows(
        IReadOnlyList<PackageHistoryRecord> packages,
        IReadOnlyList<SolutionHistoryRecord> solutions)
    {
        var rows = new List<DeployListRow>(packages.Count + solutions.Count);

        foreach (var p in packages)
        {
            rows.Add(new DeployListRow(
                Kind: "pkg",
                Id: p.Id,
                Name: p.Name,
                Status: ShortPackageStatus(p.Status),
                StartedAtUtc: p.StartedAtUtc,
                CompletedAtUtc: p.CompletedAtUtc));
        }

        foreach (var s in solutions)
        {
            rows.Add(new DeployListRow(
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

    private static void PrintRunsTable(IReadOnlyList<DeployListRow> rows)
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

    private static void PrintSolutionsTable(IReadOnlyList<InstalledSolutionRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No installed solutions found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.UniqueName.Length), 20, 48);
        int versionWidth = Math.Clamp(rows.Max(r => (r.Version ?? "").Length), 7, 20);
        int managedWidth = 7;

        string header = $"{"Unique Name".PadRight(nameWidth)} | {"Version".PadRight(versionWidth)} | {"Managed".PadRight(managedWidth)} | Friendly Name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string uniqueName = r.UniqueName.Length > nameWidth
                ? r.UniqueName[..(nameWidth - 1)] + "."
                : r.UniqueName;
            string version = string.IsNullOrWhiteSpace(r.Version) ? "(unknown)" : r.Version;
            string friendly = string.IsNullOrWhiteSpace(r.FriendlyName) ? "(none)" : r.FriendlyName;
            OutputWriter.WriteLine($"{uniqueName.PadRight(nameWidth)} | {version.PadRight(versionWidth)} | {(r.Managed ? "true" : "false").PadRight(managedWidth)} | {friendly}");
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

    private int InvalidResource()
    {
        _logger.LogError("Invalid --resource '{Resource}'. Expected 'runs' or 'solutions'.", Resource);
        return 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

/// <summary>
/// Unified row shape emitted by <c>txc deploy list --resource runs</c>.
/// Same contract in text and JSON.
/// </summary>
public sealed record DeployListRow(
    string Kind,
    Guid Id,
    string? Name,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);
