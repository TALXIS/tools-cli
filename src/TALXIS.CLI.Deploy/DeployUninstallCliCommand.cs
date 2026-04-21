using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Deploy;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall solutions from the target environment by solution name or by package run selection."
)]
public class DeployUninstallCliCommand
{
    private static readonly TimeSpan CorrelationTailBuffer = TimeSpan.FromSeconds(30);
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployUninstallCliCommand));

    [CliOption(Name = "--solution-name", Description = "Uninstall a single solution by unique name.", Required = false)]
    public string? SolutionName { get; set; }

    [CliOption(Name = "--package-name", Description = "Uninstall all correlated solutions from a package deployment run.", Required = false)]
    public string? PackageName { get; set; }

    [CliOption(Name = "--package-run-id", Description = "Specific packagehistory id to use for package uninstall mode.", Required = false)]
    public string? PackageRunId { get; set; }

    [CliOption(Name = "--latest", Description = "Use the latest run for --package-name. Ignored when --package-run-id is provided.", Required = false)]
    public bool Latest { get; set; }

    [CliOption(Name = "--yes", Description = "Confirm destructive uninstall actions.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--json", Description = "Emit uninstall result as JSON.", Required = false)]
    public bool Json { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        if (!Yes)
        {
            _logger.LogError("Uninstall is destructive. Pass --yes to confirm.");
            return 1;
        }

        int selectors = (string.IsNullOrWhiteSpace(SolutionName) ? 0 : 1) + (string.IsNullOrWhiteSpace(PackageName) ? 0 : 1);
        if (selectors != 1)
        {
            _logger.LogError("Specify exactly one selector: --solution-name or --package-name.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(SolutionName) && (!string.IsNullOrWhiteSpace(PackageRunId) || Latest))
        {
            _logger.LogError("--package-run-id and --latest are only valid with --package-name.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(PackageRunId) && !Guid.TryParse(PackageRunId, out _))
        {
            _logger.LogError("--package-run-id must be a valid GUID.");
            return 1;
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

            var uninstaller = new SolutionUninstaller(client, _logger);
            if (!string.IsNullOrWhiteSpace(SolutionName))
            {
                var outcome = await uninstaller.UninstallByUniqueNameAsync(SolutionName).ConfigureAwait(false);
                return RenderSingle(outcome);
            }

            return await RunPackageUninstallAsync(client, uninstaller).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "deploy uninstall failed");
            return 1;
        }
        finally
        {
            client?.Dispose();
            tokenProvider?.Dispose();
        }
    }

    private async Task<int> RunPackageUninstallAsync(ServiceClient client, SolutionUninstaller uninstaller)
    {
        var packageReader = new PackageHistoryReader(client, _logger);
        var historyReader = new SolutionHistoryReader(client, _logger);

        PackageHistoryRecord? packageRun;
        if (!string.IsNullOrWhiteSpace(PackageRunId))
        {
            var runId = Guid.Parse(PackageRunId);
            packageRun = await packageReader.GetByIdAsync(runId).ConfigureAwait(false);
            if (packageRun is null)
            {
                _logger.LogError("Package run not found: {PackageRunId}", runId);
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(PackageName)
                && !string.Equals(packageRun.Name, PackageName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Package run {RunId} belongs to '{RunName}', not '{RequestedName}'.", runId, packageRun.Name, PackageName);
                return 1;
            }
        }
        else
        {
            packageRun = await packageReader.GetLatestAsync(PackageName).ConfigureAwait(false);
            if (packageRun is null)
            {
                _logger.LogError("No package run found for package name '{PackageName}'.", PackageName);
                return 1;
            }
        }

        IReadOnlyList<SolutionHistoryRecord> correlated = Array.Empty<SolutionHistoryRecord>();
        if (packageRun.StartedAtUtc is { } startedAt)
        {
            if (packageRun.CorrelationId is { } corrId && corrId != Guid.Empty)
            {
                correlated = await historyReader.GetByCorrelationIdAsync(corrId).ConfigureAwait(false);
            }

            if (correlated.Count == 0)
            {
                var windowEnd = (packageRun.CompletedAtUtc ?? startedAt) + CorrelationTailBuffer;
                correlated = await historyReader.GetInTimeWindowAsync(startedAt, windowEnd).ConfigureAwait(false);
            }
        }

        var solutionNames = correlated
            .Select(s => s.SolutionName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (solutionNames.Count == 0)
        {
            _logger.LogError("No correlated solutions found for package run {RunId}.", packageRun.Id);
            return 1;
        }

        var outcomes = await uninstaller.UninstallByUniqueNamesAsync(solutionNames).ConfigureAwait(false);
        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "package",
                packageRunId = packageRun.Id,
                packageName = packageRun.Name,
                requestedPackageName = PackageName,
                solutionCount = solutionNames.Count,
                outcomes,
            }, JsonOptions));
        }
        else
        {
            Console.WriteLine($"Package run: {packageRun.Name ?? "(unknown)"}");
            Console.WriteLine($"  id: {packageRun.Id}");
            Console.WriteLine($"Correlated solutions: {solutionNames.Count}");
            foreach (var outcome in outcomes)
            {
                Console.WriteLine($"- {outcome.SolutionName}: {outcome.Status} ({outcome.Message})");
            }
        }

        return outcomes.All(o => o.Status == SolutionUninstallStatus.Success) ? 0 : 1;
    }

    private int RenderSingle(SolutionUninstallOutcome outcome)
    {
        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "solution",
                outcome,
            }, JsonOptions));
        }
        else
        {
            Console.WriteLine($"Solution: {outcome.SolutionName}");
            Console.WriteLine($"  status: {outcome.Status}");
            if (outcome.SolutionId is { } id)
            {
                Console.WriteLine($"  id: {id}");
            }
            Console.WriteLine($"  message: {outcome.Message}");
        }

        return outcome.Status == SolutionUninstallStatus.Success ? 0 : 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
