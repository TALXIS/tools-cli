using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Deploy;

/// <summary>
/// Shows details for a single deployment (package or solution run), resolved by a compact
/// <c>&lt;id&gt;</c> selector: <c>latest</c>, a full GUID, or a unique/solution name.
/// Emits findings derived from <see cref="DeployFindingsAnalyzer"/>.
/// </summary>
[CliCommand(
    Name = "show",
    Description = "Show details and findings for a single deployment. <id> accepts: latest, a full GUID, or a unique/solution name."
)]
public class DeployShowCliCommand
{
    // Small tail buffer added after package completion to catch async solution imports that
    // finish slightly after the package deployer signals done. No pre-start buffer — a solution
    // cannot belong to a package run that hasn't started yet.
    // There is no FK between packagehistory and msdyn_solutionhistory (msdyn_packagename is never
    // populated by Package Deployer; msdyn_correlationid is zero for custom PD runs). Time window
    // is the only available correlation mechanism.
    private static readonly TimeSpan CorrelationTailBuffer = TimeSpan.FromMinutes(2);

    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployShowCliCommand));

    [CliArgument(Description = "Identifier: latest, a full GUID, or a unique/solution name.")]
    public required string Id { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--full", Description = "Include every correlated solution and the formatted import log (solution mode). Default output is compact.", Required = false)]
    public bool Full { get; set; }

    [CliOption(Name = "--json", Description = "Emit the full structured record as indented JSON (always unbounded).", Required = false)]
    public bool Json { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        DeployIdSelector selector;
        try
        {
            selector = DeployIdSelector.Parse(Id);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError("{Message}", ex.Message);
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

            var pkgReader = new PackageHistoryReader(client, _logger);
            var solReader = new SolutionHistoryReader(client, _logger);

            var hit = await ResolveAsync(selector, pkgReader, solReader).ConfigureAwait(false);
            if (hit is null)
            {
                _logger.LogError("No deployment matched '{Id}'.", Id);
                return 1;
            }

            if (hit.Value.Package is { } pkg)
            {
                return await RenderPackageAsync(client, pkg).ConfigureAwait(false);
            }
            return await RenderSolutionAsync(client, hit.Value.Solution!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "deploy show failed");
            return 1;
        }
        finally
        {
            client?.Dispose();
            tokenProvider?.Dispose();
        }
    }

    private async Task<Hit?> ResolveAsync(DeployIdSelector selector, PackageHistoryReader pkgReader, SolutionHistoryReader solReader)
    {
        switch (selector.Kind)
        {
            case DeployIdSelectorKind.Latest:
            {
                var pkgTask = pkgReader.GetRecentAsync(1);
                var solTask = solReader.GetRecentAsync(1);
                await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);
                var pkg = (await pkgTask.ConfigureAwait(false)).FirstOrDefault();
                var sol = (await solTask.ConfigureAwait(false)).FirstOrDefault();
                return PickNewest(pkg, sol);
            }
            case DeployIdSelectorKind.Guid:
            {
                var pkg = await pkgReader.GetByIdAsync(selector.Guid).ConfigureAwait(false);
                if (pkg is not null) return new Hit(pkg, null);
                var sol = await solReader.GetByIdAsync(selector.Guid).ConfigureAwait(false);
                if (sol is not null) return new Hit(null, sol);
                return null;
            }
            case DeployIdSelectorKind.HexPrefix:
            {
                var pkgMatches = await pkgReader.GetByIdPrefixAsync(selector.Text).ConfigureAwait(false);
                if (pkgMatches.Count > 1)
                {
                    _logger.LogError("Prefix '{Prefix}' matches {Count} package rows. Use a longer prefix.", selector.Text, pkgMatches.Count);
                    return null;
                }
                if (pkgMatches.Count == 1)
                {
                    return new Hit(pkgMatches[0], null);
                }
                var solMatches = await solReader.GetByIdPrefixAsync(selector.Text).ConfigureAwait(false);
                if (solMatches.Count > 1)
                {
                    _logger.LogError("Prefix '{Prefix}' matches {Count} solution rows. Use a longer prefix.", selector.Text, solMatches.Count);
                    return null;
                }
                if (solMatches.Count == 1)
                {
                    return new Hit(null, solMatches[0]);
                }
                return null;
            }
            case DeployIdSelectorKind.Name:
            {
                var pkgTask = pkgReader.GetLatestAsync(selector.Text);
                var solTask = solReader.GetLatestByNameAsync(selector.Text);
                await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);
                var pkg = await pkgTask.ConfigureAwait(false);
                var sol = await solTask.ConfigureAwait(false);
                return PickNewest(pkg, sol);
            }
        }
        return null;
    }

    private static Hit? PickNewest(PackageHistoryRecord? pkg, SolutionHistoryRecord? sol)
    {
        if (pkg is null && sol is null) return null;
        if (pkg is null) return new Hit(null, sol);
        if (sol is null) return new Hit(pkg, null);
        var pkgTime = pkg.StartedAtUtc ?? DateTime.MinValue;
        var solTime = sol.StartedAtUtc ?? DateTime.MinValue;
        return pkgTime >= solTime ? new Hit(pkg, null) : new Hit(null, sol);
    }

    private async Task<int> RenderPackageAsync(ServiceClient client, PackageHistoryRecord record)
    {
        var historyReader = new SolutionHistoryReader(client, _logger);
        IReadOnlyList<SolutionHistoryRecord> correlated = Array.Empty<SolutionHistoryRecord>();
        if (record.StartedAtUtc is { } startedAt)
        {
            var windowEnd = (record.CompletedAtUtc ?? startedAt) + CorrelationTailBuffer;
            var windowStart = startedAt;
            try
            {
                correlated = await historyReader.GetInTimeWindowAsync(windowStart, windowEnd).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enrich package run with solution history.");
            }
        }

        var findings = DeployFindingsAnalyzer.Analyze(new DeployFindingsInput
        {
            ImportJobData = null,
            Primary = null,
            Solutions = correlated,
            IsPackageMode = true,
            IncludeSolutions = true,
            PackageStatus = record.Status,
            PackageStartedAtUtc = record.StartedAtUtc,
        });

        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "package",
                id = record.Id,
                name = record.Name,
                status = record.Status,
                stage = record.Stage,
                startedAtUtc = record.StartedAtUtc?.ToString("O"),
                completedAtUtc = record.CompletedAtUtc?.ToString("O"),
                operationId = record.OperationId,
                message = record.Message,
                solutions = correlated.Select(s => new
                {
                    id = s.Id,
                    solutionName = s.SolutionName,
                    solutionVersion = s.SolutionVersion,
                    operation = s.OperationLabel,
                    operationCode = s.OperationCode,
                    suboperation = s.SuboperationLabel,
                    suboperationCode = s.SuboperationCode,
                    overwriteUnmanagedCustomizations = s.OverwriteUnmanagedCustomizations,
                    startedAtUtc = s.StartedAtUtc?.ToString("O"),
                    completedAtUtc = s.CompletedAtUtc?.ToString("O"),
                    result = s.Result,
                }).ToList<object>(),
                findings,
            }, JsonOptions));
            return 0;
        }

        PrintPackage(record, correlated);
        WriteFindings(Console.Out, findings);
        return 0;
    }

    private async Task<int> RenderSolutionAsync(ServiceClient client, SolutionHistoryRecord record)
    {
        PackageHistoryRecord? parentPackage = null;
        if (record.StartedAtUtc is { } startedAt)
        {
            try
            {
                var pkgReader = new PackageHistoryReader(client, _logger);
                var nearby = await pkgReader.GetRecentAsync(50, startedAt - CorrelationTailBuffer, problemsOnly: false).ConfigureAwait(false);
                parentPackage = nearby.FirstOrDefault(p =>
                    p.StartedAtUtc is { } ps
                    && ps <= startedAt
                    && ((p.CompletedAtUtc ?? ps) + CorrelationTailBuffer) >= startedAt);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to locate parent package for solution history row.");
            }
        }

        string? formattedLog = null;
        if (Full)
        {
            try
            {
                var importReader = new ImportJobReader(client, _logger);
                if (record.StartedAtUtc is { } startedAt2)
                {
                    var windowStart = startedAt2;
                    var windowEnd = (record.CompletedAtUtc ?? startedAt2) + CorrelationTailBuffer;
                    var jobs = await importReader.GetInTimeWindowAsync(windowStart, windowEnd).ConfigureAwait(false);
                    var match = jobs.FirstOrDefault(j =>
                        record.SolutionName is not null
                        && string.Equals(j.SolutionName, record.SolutionName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        formattedLog = await importReader.GetFormattedResultsAsync(match.Id).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to retrieve formatted import log for solution history row.");
            }
        }

        var findings = DeployFindingsAnalyzer.Analyze(new DeployFindingsInput
        {
            ImportJobData = null,
            Primary = record,
            Solutions = new[] { record },
            IsPackageMode = false,
            IncludeSolutions = false,
        });

        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "solution",
                id = record.Id,
                solutionName = record.SolutionName,
                solutionVersion = record.SolutionVersion,
                packageName = record.PackageName,
                operation = record.OperationLabel,
                operationCode = record.OperationCode,
                suboperation = record.SuboperationLabel,
                suboperationCode = record.SuboperationCode,
                overwriteUnmanagedCustomizations = record.OverwriteUnmanagedCustomizations,
                startedAtUtc = record.StartedAtUtc?.ToString("O"),
                completedAtUtc = record.CompletedAtUtc?.ToString("O"),
                result = record.Result,
                parentPackage = parentPackage is null ? null : new
                {
                    id = parentPackage.Id,
                    name = parentPackage.Name,
                    status = parentPackage.Status,
                },
                formattedImportLog = formattedLog,
                findings,
            }, JsonOptions));
            return 0;
        }

        PrintSolution(record, parentPackage);
        if (Full && formattedLog is not null)
        {
            Console.WriteLine();
            Console.WriteLine("-- formatted import log --");
            Console.WriteLine(formattedLog);
        }
        WriteFindings(Console.Out, findings);
        return 0;
    }

    private static void PrintPackage(PackageHistoryRecord record, IReadOnlyList<SolutionHistoryRecord> correlated)
    {
        Console.WriteLine($"Package: {record.Name ?? "(unknown)"}");
        Console.WriteLine($"  id:              {record.Id}");
        Console.WriteLine($"  status:          {record.Status ?? "(unknown)"}");
        // Only show stage when the package didn't complete — it indicates where the failure occurred.
        bool completed = string.Equals(record.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        if (!completed && record.Stage is not null)
        {
            Console.WriteLine($"  stage:           {record.Stage}");
        }
        Console.WriteLine($"  started (UTC):   {FormatUtc(record.StartedAtUtc)}");
        if (record.CompletedAtUtc is not null)
        {
            Console.WriteLine($"  completed (UTC): {FormatUtc(record.CompletedAtUtc)}");
        }
        if (record.StartedAtUtc is { } s && record.CompletedAtUtc is { } e)
        {
            Console.WriteLine($"  duration:        {FormatDuration(e - s)}");
        }
        if (!string.IsNullOrWhiteSpace(record.Message))
        {
            Console.WriteLine($"  message:         {record.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"Solutions within package run window: {correlated.Count}");
        if (correlated.Count == 0)
        {
            return;
        }

        foreach (var solution in correlated)
        {
            string duration = (solution.StartedAtUtc is { } start && solution.CompletedAtUtc is { } end)
                ? FormatDuration(end - start)
                : "(unknown)";
            Console.WriteLine($"  - {solution.SolutionName ?? "(unknown)"} | {solution.SuboperationLabel} | {duration}");
        }
    }

    private static void PrintSolution(SolutionHistoryRecord record, PackageHistoryRecord? parent)
    {
        string context = parent is null
            ? "(standalone import)"
            : $"(part of package: {parent.Id.ToString("N")[..8]} {parent.Name})";

        Console.WriteLine($"Solution: {record.SolutionName ?? "(unknown)"} {context}");
        Console.WriteLine($"  id:              {record.Id}");
        Console.WriteLine($"  version:         {record.SolutionVersion ?? "(unknown)"}");
        Console.WriteLine($"  operation:       {record.OperationLabel} / {record.SuboperationLabel}");
        if (record.OverwriteUnmanagedCustomizations is { } overwrite)
        {
            Console.WriteLine($"  overwrite:       {(overwrite ? "yes" : "no")}");
        }
        Console.WriteLine($"  started (UTC):   {FormatUtc(record.StartedAtUtc)}");
        Console.WriteLine($"  completed (UTC): {FormatUtc(record.CompletedAtUtc)}");
        if (record.StartedAtUtc is { } s && record.CompletedAtUtc is { } e)
        {
            Console.WriteLine($"  duration:        {FormatDuration(e - s)}");
        }
        if (!string.IsNullOrWhiteSpace(record.Result))
        {
            Console.WriteLine($"  result:          {record.Result}");
        }
    }

    private static void WriteFindings(TextWriter writer, IReadOnlyList<string> findings)
    {
        if (findings is null || findings.Count == 0) return;
        writer.WriteLine();
        writer.WriteLine("Findings:");
        foreach (var f in findings)
        {
            writer.WriteLine($"- {f}");
        }
    }

    private static string FormatUtc(DateTime? value) => value is null ? "(unknown)" : value.Value.ToString("O");

    private static string FormatDuration(TimeSpan span) => span.TotalSeconds < 60
        ? $"{span.TotalSeconds:0.#}s"
        : $"{(int)span.TotalMinutes}m {span.Seconds}s";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly record struct Hit(PackageHistoryRecord? Package, SolutionHistoryRecord? Solution);
}
