using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Environment.Platforms.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Environment.Deployment;

/// <summary>
/// Shows details for a single deployment run (package or solution). Resolution is driven by
/// typed selectors — each selector maps to exactly one platform entity, with no cross-entity
/// probing. Emits findings derived from <see cref="DeploymentFindingsAnalyzer"/>.
/// </summary>
[CliCommand(
    Name = "show",
    Description = "Show details and findings for a single deployment run. Specify exactly one of --package-run-id, --solution-run-id, --async-operation-id, --package-name, --solution-name, or --latest."
)]
public class DeploymentShowCliCommand
{
    // Tail buffer added after package completion to catch async solution imports that finish
    // slightly after Package Deployer signals done.
    private static readonly TimeSpan CorrelationTailBuffer = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeploymentShowCliCommand));

    [CliOption(Name = "--package-run-id", Description = "GUID of a package deployment run (packagehistory row).", Required = false)]
    public string? PackageRunId { get; set; }

    [CliOption(Name = "--solution-run-id", Description = "GUID of a solution import run (msdyn_solutionhistory row).", Required = false)]
    public string? SolutionRunId { get; set; }

    [CliOption(Name = "--async-operation-id", Description = "GUID of the async operation returned by a queued solution import. Falls back to the correlated solution history row, then to raw async-op status.", Required = false)]
    public string? AsyncOperationId { get; set; }

    [CliOption(Name = "--package-name", Description = "NuGet package name — returns the most recent run in packagehistory matching this name.", Required = false)]
    public string? PackageName { get; set; }

    [CliOption(Name = "--solution-name", Description = "Solution unique name — returns the most recent standalone solution import matching this name.", Required = false)]
    public string? SolutionName { get; set; }

    [CliOption(Name = "--latest", Description = "Show the most recent deployment run across packages and solutions.", Required = false)]
    public bool Latest { get; set; }

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
        int specified =
            (PackageRunId is not null ? 1 : 0) +
            (SolutionRunId is not null ? 1 : 0) +
            (AsyncOperationId is not null ? 1 : 0) +
            (PackageName is not null ? 1 : 0) +
            (SolutionName is not null ? 1 : 0) +
            (Latest ? 1 : 0);

        if (specified == 0)
        {
            _logger.LogError("Specify exactly one of --package-run-id, --solution-run-id, --async-operation-id, --package-name, --solution-name, or --latest.");
            return 1;
        }
        if (specified > 1)
        {
            _logger.LogError("--package-run-id, --solution-run-id, --async-operation-id, --package-name, --solution-name, and --latest are mutually exclusive. Specify only one.");
            return 1;
        }

        Guid packageRunGuid = Guid.Empty;
        Guid solutionRunGuid = Guid.Empty;
        Guid asyncOpGuid = Guid.Empty;

        if (PackageRunId is not null && !TryParseGuid(PackageRunId, "--package-run-id", out packageRunGuid)) return 1;
        if (SolutionRunId is not null && !TryParseGuid(SolutionRunId, "--solution-run-id", out solutionRunGuid)) return 1;
        if (AsyncOperationId is not null && !TryParseGuid(AsyncOperationId, "--async-operation-id", out asyncOpGuid)) return 1;

        if (PackageName is not null && string.IsNullOrWhiteSpace(PackageName))
        {
            _logger.LogError("--package-name must not be empty.");
            return 1;
        }
        if (SolutionName is not null && string.IsNullOrWhiteSpace(SolutionName))
        {
            _logger.LogError("--solution-name must not be empty.");
            return 1;
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
            var client = conn.Client;
            try
            {
                var pkgReader = new PackageHistoryReader(client, _logger);
                var solReader = new SolutionHistoryReader(client, _logger);

                if (PackageRunId is not null)
                {
                    var pkg = await pkgReader.GetByIdAsync(packageRunGuid).ConfigureAwait(false);
                    if (pkg is null)
                    {
                        _logger.LogError("No package run matched --package-run-id '{Id}'.", PackageRunId);
                        return 1;
                    }
                    return await RenderPackageAsync(client, pkg).ConfigureAwait(false);
                }

                if (SolutionRunId is not null)
                {
                    var sol = await solReader.GetByIdAsync(solutionRunGuid).ConfigureAwait(false);
                    if (sol is null)
                    {
                        _logger.LogError("No solution run matched --solution-run-id '{Id}'.", SolutionRunId);
                        return 1;
                    }
                    return await RenderSolutionAsync(client, sol).ConfigureAwait(false);
                }

                if (AsyncOperationId is not null)
                {
                    var sol = await solReader.GetByActivityIdAsync(asyncOpGuid).ConfigureAwait(false);
                    if (sol is not null)
                    {
                        return await RenderSolutionAsync(client, sol).ConfigureAwait(false);
                    }
                    int? asyncResult = await TryShowAsyncOperationAsync(client, asyncOpGuid).ConfigureAwait(false);
                    if (asyncResult.HasValue) return asyncResult.Value;
                    _logger.LogError("No async operation or correlated solution run matched --async-operation-id '{Id}'.", AsyncOperationId);
                    return 1;
                }

                if (PackageName is not null)
                {
                    var pkg = await pkgReader.GetLatestAsync(PackageName!.Trim()).ConfigureAwait(false);
                    if (pkg is null)
                    {
                        _logger.LogError("No package run matched --package-name '{Name}'.", PackageName);
                        return 1;
                    }
                    return await RenderPackageAsync(client, pkg).ConfigureAwait(false);
                }

                if (SolutionName is not null)
                {
                    var sol = await solReader.GetLatestByNameAsync(SolutionName!.Trim()).ConfigureAwait(false);
                    if (sol is null)
                    {
                        _logger.LogError("No solution run matched --solution-name '{Name}'.", SolutionName);
                        return 1;
                    }
                    return await RenderSolutionAsync(client, sol).ConfigureAwait(false);
                }

                // --latest
                var pkgTask = pkgReader.GetRecentAsync(1);
                var solTask = solReader.GetRecentAsync(1);
                await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);
                var latestPkg = (await pkgTask.ConfigureAwait(false)).FirstOrDefault();
                var latestSol = (await solTask.ConfigureAwait(false)).FirstOrDefault();
                if (latestPkg is null && latestSol is null)
                {
                    _logger.LogError("No deployment runs found.");
                    return 1;
                }
                var pkgTime = latestPkg?.StartedAtUtc ?? DateTime.MinValue;
                var solTime = latestSol?.StartedAtUtc ?? DateTime.MinValue;
                if (latestPkg is not null && (latestSol is null || pkgTime >= solTime))
                {
                    return await RenderPackageAsync(client, latestPkg).ConfigureAwait(false);
                }
                return await RenderSolutionAsync(client, latestSol!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "environment deployment show failed");
                return 1;
            }
        }
    }

    private bool TryParseGuid(string value, string optionName, out Guid guid)
    {
        if (Guid.TryParse(value, out guid)) return true;
        _logger.LogError("{Option} must be a full GUID.", optionName);
        return false;
    }

    private async Task<int> RenderPackageAsync(ServiceClient client, PackageHistoryRecord record)
    {
        var historyReader = new SolutionHistoryReader(client, _logger);
        IReadOnlyList<SolutionHistoryRecord> correlated = Array.Empty<SolutionHistoryRecord>();
        if (record.StartedAtUtc is { } startedAt)
        {
            try
            {
                // Preferred path: exact join via asyncoperation.correlationid.
                if (record.CorrelationId is { } corrId && corrId != Guid.Empty)
                {
                    correlated = await historyReader.GetByCorrelationIdAsync(corrId).ConfigureAwait(false);
                }

                // Fallback: time window when asyncoperation records have been cleaned up.
                if (correlated.Count == 0)
                {
                    var windowEnd = (record.CompletedAtUtc ?? startedAt) + CorrelationTailBuffer;
                    correlated = await historyReader.GetInTimeWindowAsync(startedAt, windowEnd).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enrich package run with solution history.");
            }
        }

        var findings = DeploymentFindingsAnalyzer.Analyze(new DeploymentFindingsInput
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
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "package",
                id = record.Id,
                name = record.Name,
                status = record.Status,
                stage = record.Stage,
                startedAtUtc = record.StartedAtUtc?.ToString("O"),
                completedAtUtc = record.CompletedAtUtc?.ToString("O"),
                operationId = record.OperationId,
                correlationId = record.CorrelationId,
                message = record.Message,
                solutions = correlated.Select(s => new
                {
                    id = s.Id,
                    activityId = s.ActivityId,
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
        ImportJobRecord? importJobMatch = null;
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
                    importJobMatch = jobs.FirstOrDefault(j =>
                        record.SolutionName is not null
                        && string.Equals(j.SolutionName, record.SolutionName, StringComparison.OrdinalIgnoreCase));
                    if (importJobMatch is not null)
                    {
                        formattedLog = await importReader.GetFormattedResultsAsync(importJobMatch.Id).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to retrieve formatted import log for solution history row.");
            }
        }

        var findings = DeploymentFindingsAnalyzer.Analyze(new DeploymentFindingsInput
        {
            ImportJobData = importJobMatch?.Data,
            Primary = record,
            Solutions = new[] { record },
            IsPackageMode = false,
            IncludeSolutions = false,
        });

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
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
            OutputWriter.WriteLine();
            OutputWriter.WriteLine("-- formatted import log --");
            OutputWriter.WriteLine(formattedLog);
        }
        WriteFindings(Console.Out, findings);
        return 0;
    }

    /// <summary>
    /// Attempts to resolve and display status for an <c>asyncoperation</c> row directly.
    /// Returns the exit code (0 = success, 1 = import failed) when the GUID is a known async
    /// operation ID, or <c>null</c> when the GUID does not correspond to any async operation.
    /// </summary>
    private async Task<int?> TryShowAsyncOperationAsync(ServiceClient client, Guid asyncOpId)
    {
        Entity entity;
        try
        {
            entity = await client.RetrieveAsync(
                DataverseSchema.AsyncOperation.EntityName,
                asyncOpId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(
                    "statecode", "statuscode", "message", "friendlymessage", "createdon", "completedon"),
                default).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to retrieve asyncoperation {Id}.", asyncOpId);
            return null;
        }

        var statecode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        var statuscode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;

        // Not yet completed — show live status.
        if (statecode != 3)
        {
            string stateLabel = statecode switch
            {
                0 => "Ready",
                1 => "Suspended",
                2 => "In Progress",
                _ => $"State {statecode}",
            };

            if (Json)
            {
                OutputWriter.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "asyncoperation",
                    id = asyncOpId,
                    state = stateLabel,
                    statecode,
                    statuscode,
                    completed = false,
                }, JsonOptions));
            }
            else
            {
                OutputWriter.WriteLine($"Import in progress: {stateLabel}");
                OutputWriter.WriteLine($"  asyncOperationId: {asyncOpId}");
                OutputWriter.WriteLine($"  Run again to refresh or use `txc environment deployment show --async-operation-id {asyncOpId}` when done.");
            }
            return 0;
        }

        bool succeededOp = statuscode == 30;
        DateTime? completedOn = entity.Contains("completedon")
            ? entity.GetAttributeValue<DateTime>("completedon")
            : null;
        DateTime? createdOn = entity.Contains("createdon")
            ? entity.GetAttributeValue<DateTime>("createdon")
            : null;

        var historyReader = new SolutionHistoryReader(client, _logger);
        DateTime pivot = completedOn ?? createdOn ?? DateTime.UtcNow;
        SolutionHistoryRecord? sol = null;

        bool veryRecent = (DateTime.UtcNow - pivot).TotalSeconds < 60;
        int attempts = veryRecent ? 3 : 1;
        for (int i = 0; i < attempts; i++)
        {
            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            sol = await historyReader.GetByActivityIdAsync(asyncOpId, nearUtc: pivot).ConfigureAwait(false);
            if (sol is not null) break;
        }

        if (sol is not null)
        {
            return await RenderSolutionAsync(client, sol).ConfigureAwait(false);
        }

        string? message = entity.GetAttributeValue<string>("friendlymessage")
            ?? entity.GetAttributeValue<string>("message");

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "asyncoperation",
                id = asyncOpId,
                state = "Completed",
                statecode,
                statuscode,
                completed = true,
                succeeded = succeededOp,
                message,
            }, JsonOptions));
        }
        else
        {
            OutputWriter.WriteLine($"Async operation {asyncOpId}");
            OutputWriter.WriteLine($"  state:   Completed");
            OutputWriter.WriteLine($"  result:  {(succeededOp ? "Succeeded" : $"Failed (status {statuscode})")}");
            if (!string.IsNullOrWhiteSpace(message))
            {
                OutputWriter.WriteLine($"  message: {message}");
            }
            OutputWriter.WriteLine("  (Solution history record not yet available — re-run shortly to get full details.)");
        }

        return succeededOp ? 0 : 1;
    }

    private static bool IsNotFoundError(Exception ex)
    {
        if (ex.Message.Contains("0x80040217", StringComparison.OrdinalIgnoreCase)) return true;
        if (ex.Message.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase)) return true;
        if (ex.Message.Contains("ObjectDoesNotExist", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void PrintPackage(PackageHistoryRecord record, IReadOnlyList<SolutionHistoryRecord> correlated)
    {
        OutputWriter.WriteLine($"Package: {record.Name ?? "(unknown)"}");
        OutputWriter.WriteLine($"  id:              {record.Id}");
        OutputWriter.WriteLine($"  status:          {record.Status ?? "(unknown)"}");
        bool completed = string.Equals(record.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        if (!completed && record.Stage is not null)
        {
            OutputWriter.WriteLine($"  stage:           {record.Stage}");
        }
        OutputWriter.WriteLine($"  started (UTC):   {FormatUtc(record.StartedAtUtc)}");
        if (record.CompletedAtUtc is not null)
        {
            OutputWriter.WriteLine($"  completed (UTC): {FormatUtc(record.CompletedAtUtc)}");
        }
        if (record.StartedAtUtc is { } s && record.CompletedAtUtc is { } e)
        {
            OutputWriter.WriteLine($"  duration:        {FormatDuration(e - s)}");
        }
        if (!string.IsNullOrWhiteSpace(record.Message))
        {
            OutputWriter.WriteLine($"  message:         {record.Message}");
        }

        OutputWriter.WriteLine();
        OutputWriter.WriteLine($"Solutions within package run window: {correlated.Count}");
        if (correlated.Count == 0)
        {
            return;
        }

        foreach (var solution in correlated)
        {
            string duration = (solution.StartedAtUtc is { } start && solution.CompletedAtUtc is { } end)
                ? FormatDuration(end - start)
                : "(unknown)";
            OutputWriter.WriteLine($"  - {solution.SolutionName ?? "(unknown)"} | {solution.SuboperationLabel} | {duration}");
        }
    }

    private static void PrintSolution(SolutionHistoryRecord record, PackageHistoryRecord? parent)
    {
        string context = parent is null
            ? "(standalone import)"
            : $"(part of package: {parent.Id.ToString("N")[..8]} {parent.Name})";

        OutputWriter.WriteLine($"Solution: {record.SolutionName ?? "(unknown)"} {context}");
        OutputWriter.WriteLine($"  id:              {record.Id}");
        OutputWriter.WriteLine($"  version:         {record.SolutionVersion ?? "(unknown)"}");
        OutputWriter.WriteLine($"  operation:       {record.OperationLabel} / {record.SuboperationLabel}");
        if (record.OverwriteUnmanagedCustomizations is { } overwrite)
        {
            OutputWriter.WriteLine($"  overwrite:       {(overwrite ? "yes" : "no")}");
        }
        OutputWriter.WriteLine($"  started (UTC):   {FormatUtc(record.StartedAtUtc)}");
        OutputWriter.WriteLine($"  completed (UTC): {FormatUtc(record.CompletedAtUtc)}");
        if (record.StartedAtUtc is { } s && record.CompletedAtUtc is { } e)
        {
            OutputWriter.WriteLine($"  duration:        {FormatDuration(e - s)}");
        }
        if (!string.IsNullOrWhiteSpace(record.Result))
        {
            OutputWriter.WriteLine($"  result:          {record.Result}");
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
}
