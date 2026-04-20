using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Deploy;

/// <summary>
/// Lists recent Dataverse deployments across both streams:
/// <c>packagehistory</c> (Package Deployer runs) and <c>msdyn_solutionhistory</c>
/// (solution imports, including standalone). Output is interleaved by start time.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List recent deployments (packages and solutions) against a Dataverse environment."
)]
public class DeployListCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployListCliCommand));

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--since", Description = "Relative time window, e.g. 30m, 24h, 7d, 2w. When omitted, the last 20 rows across both streams are returned.", Required = false)]
    public string? Since { get; set; }

    [CliOption(Name = "--problems", Description = "Only rows that are not Success/Completed (includes in-progress, failed, and stuck).", Required = false)]
    public bool Problems { get; set; }

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
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

        string? connectionString = ServiceClientFactory.ResolveConnectionString(ConnectionString);
        string? environmentUrl = ServiceClientFactory.ResolveEnvironmentUrl(EnvironmentUrl);

        if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(environmentUrl))
        {
            _logger.LogError("Dataverse authentication is required. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
            return 1;
        }

        ServiceClient? client = null;
        DataverseAuthTokenProvider? tokenProvider = null;
        try
        {
            client = ServiceClientFactory.Create(
                connectionString,
                environmentUrl,
                DeviceCode,
                Verbose,
                _logger,
                out tokenProvider);

            var pkgReader = new PackageHistoryReader(client, _logger);
            var solReader = new SolutionHistoryReader(client, _logger);

            var pkgTask = pkgReader.GetRecentAsync(defaultCount, sinceUtc, Problems);
            var solTask = solReader.GetRecentAsync(defaultCount, sinceUtc, Problems);
            await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);

            var rows = BuildRows(await pkgTask.ConfigureAwait(false), await solTask.ConfigureAwait(false));
            int max = sinceUtc is null ? 20 : rows.Count;
            var trimmed = rows.Take(max).ToList();

            if (Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(trimmed, JsonOptions));
                return 0;
            }

            PrintTable(trimmed);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "deploy list failed");
            return 1;
        }
        finally
        {
            client?.Dispose();
            tokenProvider?.Dispose();
        }
    }

    /// <summary>
    /// Normalizes and merges both streams into a single list sorted by start time desc.
    /// Exposed for unit tests.
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

    private static void PrintTable(IReadOnlyList<DeployListRow> rows)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine("No deployments found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => (r.Name ?? "").Length), 12, 40);
        int statusWidth = Math.Max(8, rows.Max(r => r.Status.Length));

        string header = $"Kind | {"Name".PadRight(nameWidth)} | {"Status".PadRight(statusWidth)} | {"Started (UTC)",-19} | Duration";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string name = (r.Name ?? "(unknown)");
            if (name.Length > nameWidth) name = name[..(nameWidth - 1)] + ".";
            string started = r.StartedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(unknown)";
            string duration = FormatDuration(r.StartedAtUtc, r.CompletedAtUtc);
            Console.WriteLine($"{r.Kind,-4} | {name.PadRight(nameWidth)} | {r.Status.PadRight(statusWidth)} | {started,-19} | {duration}");
        }

        Console.WriteLine();
        Console.WriteLine("Use: txc deploy show <name|latest|guid> for details.");
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
/// Unified row shape emitted by <c>txc deploy list</c>. Same contract in text and JSON.
/// </summary>
public sealed record DeployListRow(
    string Kind,
    Guid Id,
    string? Name,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);
