using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Deployment;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get details and findings for a single deployment run. Specify exactly one of --package-run-id, --solution-run-id, --async-operation-id, --package-name, --solution-name, or --latest."
)]
public class DeploymentGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(DeploymentGetCliCommand));

    [CliOption(Name = "--package-run-id", Description = "GUID of a package deployment run (packagehistory row).", Required = false)]
    public string? PackageRunId { get; set; }

    [CliOption(Name = "--solution-run-id", Description = "GUID of a solution import run (msdyn_solutionhistory row).", Required = false)]
    public string? SolutionRunId { get; set; }

    [CliOption(Name = "--async-operation-id", Description = "GUID of the async operation returned by a queued solution import. Falls back to the correlated solution history row, then to raw async-op status.", Required = false)]
    public string? AsyncOperationId { get; set; }

    [CliOption(Name = "--package-name", Description = "NuGet package name returns the most recent run in packagehistory matching this name.", Required = false)]
    public string? PackageName { get; set; }

    [CliOption(Name = "--solution-name", Description = "Solution unique name returns the most recent standalone solution import matching this name.", Required = false)]
    public string? SolutionName { get; set; }

    [CliOption(Name = "--latest", Description = "Show the most recent deployment run across packages and solutions.", Required = false)]
    public bool Latest { get; set; }

    [CliOption(Name = "--full", Description = "Include every correlated solution and the formatted import log (solution mode). Default output is compact.", Required = false)]
    public bool Full { get; set; }

    protected override async Task<int> ExecuteAsync()
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
            Logger.LogError("Specify exactly one of --package-run-id, --solution-run-id, --async-operation-id, --package-name, --solution-name, or --latest.");
            return ExitValidationError;
        }
        if (specified > 1)
        {
            Logger.LogError("--package-run-id, --solution-run-id, --async-operation-id, --package-name, --solution-name, and --latest are mutually exclusive. Specify only one.");
            return ExitValidationError;
        }

        Guid packageRunGuid = Guid.Empty, solutionRunGuid = Guid.Empty, asyncOpGuid = Guid.Empty;
        if (PackageRunId is not null && !TryParseGuid(PackageRunId, "--package-run-id", out packageRunGuid)) return ExitValidationError;
        if (SolutionRunId is not null && !TryParseGuid(SolutionRunId, "--solution-run-id", out solutionRunGuid)) return ExitValidationError;
        if (AsyncOperationId is not null && !TryParseGuid(AsyncOperationId, "--async-operation-id", out asyncOpGuid)) return ExitValidationError;
        if (PackageName is not null && string.IsNullOrWhiteSpace(PackageName)) { Logger.LogError("--package-name must not be empty."); return ExitValidationError; }
        if (SolutionName is not null && string.IsNullOrWhiteSpace(SolutionName)) { Logger.LogError("--solution-name must not be empty."); return ExitValidationError; }

        var service = TxcServices.Get<IDeploymentDetailService>();
        var ct = CancellationToken.None;
        var result = PackageRunId is not null ? await service.GetByPackageRunIdAsync(Profile, packageRunGuid, ct).ConfigureAwait(false)
            : SolutionRunId is not null ? await service.GetBySolutionRunIdAsync(Profile, solutionRunGuid, Full, ct).ConfigureAwait(false)
            : AsyncOperationId is not null ? await service.GetByAsyncOperationIdAsync(Profile, asyncOpGuid, Full, ct).ConfigureAwait(false)
            : PackageName is not null ? await service.GetLatestByPackageNameAsync(Profile, PackageName!.Trim(), ct).ConfigureAwait(false)
            : SolutionName is not null ? await service.GetLatestBySolutionNameAsync(Profile, SolutionName!.Trim(), Full, ct).ConfigureAwait(false)
            : await service.GetLatestAsync(Profile, Full, ct).ConfigureAwait(false);

        if (result is null)
        {
            string hint = PackageRunId is not null ? $"No package run matched --package-run-id '{PackageRunId}'."
                : SolutionRunId is not null ? $"No solution run matched --solution-run-id '{SolutionRunId}'."
                : AsyncOperationId is not null ? $"No async operation or correlated solution run matched --async-operation-id '{AsyncOperationId}'."
                : PackageName is not null ? $"No package run matched --package-name '{PackageName}'."
                : SolutionName is not null ? $"No solution run matched --solution-name '{SolutionName}'."
                : "No deployment runs found.";
            Logger.LogError("{Message}", hint);
            return ExitError;
        }

        return result.Kind switch
        {
            DeploymentRunKind.Package => RenderPackage(result),
            DeploymentRunKind.Solution => RenderSolution(result),
            DeploymentRunKind.AsyncOperationInProgress => RenderAsyncInProgress(result),
            DeploymentRunKind.AsyncOperationCompleted => RenderAsyncCompleted(result),
            _ => ExitError,
        };
    }

    private bool TryParseGuid(string value, string optionName, out Guid guid)
    {
        if (Guid.TryParse(value, out guid)) return true;
        Logger.LogError("{Option} must be a full GUID.", optionName);
        return false;
    }

    // TODO: Refactor Render*/Print*/WriteFindings methods below to use OutputFormatter instead of manual OutputContext.IsJson branching.
#pragma warning disable TXC003
    private int RenderPackage(DeploymentDetailResult r)
    {
        var pkg = r.Package!;
        var correlated = r.CorrelatedSolutions;
        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "package",
                id = pkg.Id,
                name = pkg.Name,
                status = pkg.Status,
                stage = pkg.Stage,
                startedAtUtc = pkg.StartedAtUtc?.ToString("O"),
                completedAtUtc = pkg.CompletedAtUtc?.ToString("O"),
                operationId = pkg.OperationId,
                correlationId = pkg.CorrelationId,
                message = pkg.Message,
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
                findings = r.Findings,
            }, TxcOutputJsonOptions.Default));
            return ExitSuccess;
        }
        PrintPackage(pkg, correlated);
        WriteFindings(r.Findings);
        return ExitSuccess;
    }

    private int RenderSolution(DeploymentDetailResult r)
    {
        var sol = r.Solution!;
        var parent = r.ParentPackage;
        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "solution",
                id = sol.Id,
                solutionName = sol.SolutionName,
                solutionVersion = sol.SolutionVersion,
                packageName = sol.PackageName,
                operation = sol.OperationLabel,
                operationCode = sol.OperationCode,
                suboperation = sol.SuboperationLabel,
                suboperationCode = sol.SuboperationCode,
                overwriteUnmanagedCustomizations = sol.OverwriteUnmanagedCustomizations,
                startedAtUtc = sol.StartedAtUtc?.ToString("O"),
                completedAtUtc = sol.CompletedAtUtc?.ToString("O"),
                result = sol.Result,
                parentPackage = parent is null ? null : new { id = parent.Id, name = parent.Name, status = parent.Status },
                formattedImportLog = r.FormattedImportLog,
                findings = r.Findings,
            }, TxcOutputJsonOptions.Default));
            return ExitSuccess;
        }
        PrintSolution(sol, parent);
        if (Full && r.FormattedImportLog is not null)
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine("-- formatted import log --");
            OutputWriter.WriteLine(r.FormattedImportLog);
        }
        WriteFindings(r.Findings);
        return ExitSuccess;
    }

    private int RenderAsyncInProgress(DeploymentDetailResult r)
    {
        var op = r.AsyncOperation!;
        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "asyncoperation",
                id = op.Id,
                state = op.StateLabel,
                statecode = op.StateCode,
                statuscode = op.StatusCode,
                completed = false,
            }, TxcOutputJsonOptions.Default));
        }
        else
        {
            OutputWriter.WriteLine($"Import in progress: {op.StateLabel}");
            OutputWriter.WriteLine($"  asyncOperationId: {op.Id}");
            OutputWriter.WriteLine($"  Run again to refresh or use `txc environment deployment show --async-operation-id {op.Id}` when done.");
        }
        return ExitSuccess;
    }

    private int RenderAsyncCompleted(DeploymentDetailResult r)
    {
        var op = r.AsyncOperation!;
        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "asyncoperation",
                id = op.Id,
                state = "Completed",
                statecode = op.StateCode,
                statuscode = op.StatusCode,
                completed = true,
                succeeded = op.Succeeded,
                message = op.Message,
            }, TxcOutputJsonOptions.Default));
        }
        else
        {
            OutputWriter.WriteLine($"Async operation {op.Id}");
            OutputWriter.WriteLine($"  state:   Completed");
            OutputWriter.WriteLine($"  result:  {(op.Succeeded ? "Succeeded" : $"Failed (status {op.StatusCode})")}");
            if (!string.IsNullOrWhiteSpace(op.Message))
            {
                OutputWriter.WriteLine($"  message: {op.Message}");
            }
            OutputWriter.WriteLine("  (Solution history record not yet available -- re-run shortly to get full details.)");
        }
        return op.Succeeded ? ExitSuccess : ExitError;
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
        if (correlated.Count == 0) return;
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

    private static void WriteFindings(IReadOnlyList<string> findings)
    {
        if (findings is null || findings.Count == 0) return;
        OutputWriter.WriteLine();
        OutputWriter.WriteLine("Findings:");
        foreach (var f in findings)
        {
            OutputWriter.WriteLine($"- {f}");
        }
    }
#pragma warning restore TXC003

    private static string FormatUtc(DateTime? value) => value is null ? "(unknown)" : value.Value.ToString("O");

    private static string FormatDuration(TimeSpan span) => span.TotalSeconds < 60
        ? $"{span.TotalSeconds:0.#}s"
        : $"{(int)span.TotalMinutes}m {span.Seconds}s";

}
