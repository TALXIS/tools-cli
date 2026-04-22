using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Deploy;

/// <summary>
/// Shows details for a single deployment (package or solution run), resolved by a compact
/// <c>&lt;id&gt;</c> selector: <c>latest</c>, a full GUID, or a unique/solution name.
/// Emits findings derived from <see cref="DeployFindingsAnalyzer"/>.
/// </summary>
[CliCommand(
    Name = "show",
    Description = "Show details and findings for a single deployment (package or solution run). Specify exactly one of --id, --package-name, --solution-name, or --latest."
)]
public class DeployShowCliCommand
{
    // Tail buffer added after package completion to catch async solution imports that finish
    // slightly after Package Deployer signals done.
    //
    // Preferred correlation path (for recent deployments, up to ~30 days):
    //   packagehistory.correlationid → asyncoperation.correlationid → asyncoperation.asyncoperationid
    //   → msdyn_solutionhistory.msdyn_activityid  (exact, handles concurrent imports correctly)
    //
    // Time window is used as fallback when asyncoperation records have been cleaned up by
    // Dataverse's async job retention policy.
    private static readonly TimeSpan CorrelationTailBuffer = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployShowCliCommand));

    [CliOption(Name = "--id", Description = "Full GUID of a deployment record (msdyn_solutionhistoryid, packagehistory id) or the asyncOperationId returned by a queued solution import.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--package-name", Description = "NuGet package name — returns the most recent run in packagehistory matching this name (only reliable for packages deployed via txc).", Required = false)]
    public string? PackageName { get; set; }

    [CliOption(Name = "--solution-name", Description = "Solution unique name — returns the most recent standalone solution import matching this name.", Required = false)]
    public string? SolutionName { get; set; }

    [CliOption(Name = "--latest", Description = "Show the most recent deployment across packages and solutions.", Required = false)]
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
        DeployIdSelector selector;
        int specified = (Id is not null ? 1 : 0) + (PackageName is not null ? 1 : 0) + (SolutionName is not null ? 1 : 0) + (Latest ? 1 : 0);
        if (specified == 0)
        {
            _logger.LogError("Specify exactly one of --id, --package-name, --solution-name, or --latest.");
            return 1;
        }
        if (specified > 1)
        {
            _logger.LogError("--id, --package-name, --solution-name, and --latest are mutually exclusive. Specify only one.");
            return 1;
        }

        if (Latest)
        {
            selector = DeployIdSelector.Parse("latest");
        }
        else if (PackageName is { } pkgName)
        {
            if (string.IsNullOrWhiteSpace(pkgName))
            {
                _logger.LogError("--package-name must not be empty.");
                return 1;
            }
            selector = new DeployIdSelector(DeployIdSelectorKind.PackageName, Guid.Empty, pkgName);
        }
        else if (SolutionName is { } solName)
        {
            if (string.IsNullOrWhiteSpace(solName))
            {
                _logger.LogError("--solution-name must not be empty.");
                return 1;
            }
            selector = new DeployIdSelector(DeployIdSelectorKind.SolutionName, Guid.Empty, solName);
        }
        else
        {
            // --id: must be a full GUID (or asyncOperationId)
            if (!Guid.TryParse(Id, out var guid))
            {
                _logger.LogError("--id must be a full GUID (e.g. the asyncOperationId returned by a queued import or a deployment record id).");
                return 1;
            }
            selector = new DeployIdSelector(DeployIdSelectorKind.Guid, guid, Id.Trim());
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

                var hit = await ResolveAsync(selector, pkgReader, solReader).ConfigureAwait(false);
                if (hit is null)
                {
                    // If the GUID didn't resolve to a history record it may be an asyncOperationId
                    // for an import that is still running or just completed (race with history write).
                    if (selector.Kind == DeployIdSelectorKind.Guid)
                    {
                        int? asyncResult = await TryShowAsyncOperationAsync(client, selector.Guid).ConfigureAwait(false);
                        if (asyncResult.HasValue) return asyncResult.Value;
                    }
                    string missingId = Id ?? PackageName ?? SolutionName ?? "(latest)";
                    _logger.LogError("No deployment matched '{Id}'.", missingId);
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
                // The GUID might be an asyncOperationId (from deploy run --type solution).
                var solByActivity = await solReader.GetByActivityIdAsync(selector.Guid).ConfigureAwait(false);
                if (solByActivity is not null) return new Hit(null, solByActivity);
                return null;
            }
            case DeployIdSelectorKind.PackageName:
            {
                // Search packagehistory.uniquename only (reliable for NuGet deploys via txc).
                var pkg = await pkgReader.GetLatestAsync(selector.Text).ConfigureAwait(false);
                return pkg is not null ? new Hit(pkg, null) : null;
            }
            case DeployIdSelectorKind.SolutionName:
            {
                // Search msdyn_solutionhistory by solution unique name only.
                var sol = await solReader.GetLatestByNameAsync(selector.Text).ConfigureAwait(false);
                return sol is not null ? new Hit(null, sol) : null;
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
            try
            {
                // Preferred path: exact join via asyncoperation.correlationid.
                // Works for deployments within Dataverse's async job retention window (~30 days).
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

        var findings = DeployFindingsAnalyzer.Analyze(new DeployFindingsInput
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
    /// operation ID, or <c>null</c> when the GUID does not correspond to any async operation
    /// (caller should fall through to "No deployment matched").
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
                default).ConfigureAwait(false);        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return null; // Not an asyncoperation GUID at all — caller continues to error.
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
                OutputWriter.WriteLine($"  Run again to refresh or use `txc deploy show --id {asyncOpId}` when done.");
            }
            return 0;
        }

        // Completed — try to fetch solution history. When the operation finished very recently
        // the history record may not be written yet; retry briefly before giving up.
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

        // Fallback: history record unavailable — surface raw async op status.
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
        // ServiceClient throws various exception types for 404. Check message as fallback.
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
        // Only show stage when the package didn't complete — it indicates where the failure occurred.
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

    private readonly record struct Hit(PackageHistoryRecord? Package, SolutionHistoryRecord? Solution);
}
