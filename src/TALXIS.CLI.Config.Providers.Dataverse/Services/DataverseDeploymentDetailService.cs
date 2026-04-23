using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Config.Providers.Dataverse.Services;

internal sealed class DataverseDeploymentDetailService : IDeploymentDetailService
{
    private static readonly TimeSpan CorrelationTailBuffer = TimeSpan.FromSeconds(30);

    public async Task<DeploymentDetailResult?> GetByPackageRunIdAsync(string? profileName, Guid id, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentDetailService));
        var pkg = await new PackageHistoryReader(conn.Client, logger).GetByIdAsync(id).ConfigureAwait(false);
        return pkg is null ? null : await BuildPackageAsync(conn.Client, pkg, logger).ConfigureAwait(false);
    }

    public async Task<DeploymentDetailResult?> GetBySolutionRunIdAsync(string? profileName, Guid id, bool includeFull, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentDetailService));
        var sol = await new SolutionHistoryReader(conn.Client, logger).GetByIdAsync(id).ConfigureAwait(false);
        return sol is null ? null : await BuildSolutionAsync(conn.Client, sol, includeFull, logger).ConfigureAwait(false);
    }

    public async Task<DeploymentDetailResult?> GetByAsyncOperationIdAsync(string? profileName, Guid id, bool includeFull, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentDetailService));
        var solReader = new SolutionHistoryReader(conn.Client, logger);
        var sol = await solReader.GetByActivityIdAsync(id).ConfigureAwait(false);
        if (sol is not null)
        {
            return await BuildSolutionAsync(conn.Client, sol, includeFull, logger).ConfigureAwait(false);
        }
        return await TryBuildAsyncOperationAsync(conn.Client, id, includeFull, logger).ConfigureAwait(false);
    }

    public async Task<DeploymentDetailResult?> GetLatestByPackageNameAsync(string? profileName, string packageName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentDetailService));
        var pkg = await new PackageHistoryReader(conn.Client, logger).GetLatestAsync(packageName).ConfigureAwait(false);
        return pkg is null ? null : await BuildPackageAsync(conn.Client, pkg, logger).ConfigureAwait(false);
    }

    public async Task<DeploymentDetailResult?> GetLatestBySolutionNameAsync(string? profileName, string solutionName, bool includeFull, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentDetailService));
        var sol = await new SolutionHistoryReader(conn.Client, logger).GetLatestByNameAsync(solutionName).ConfigureAwait(false);
        return sol is null ? null : await BuildSolutionAsync(conn.Client, sol, includeFull, logger).ConfigureAwait(false);
    }

    public async Task<DeploymentDetailResult?> GetLatestAsync(string? profileName, bool includeFull, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataverseDeploymentDetailService));
        var pkgReader = new PackageHistoryReader(conn.Client, logger);
        var solReader = new SolutionHistoryReader(conn.Client, logger);
        var pkgTask = pkgReader.GetRecentAsync(1);
        var solTask = solReader.GetRecentAsync(1);
        await Task.WhenAll(pkgTask, solTask).ConfigureAwait(false);
        var pkg = (await pkgTask.ConfigureAwait(false)).FirstOrDefault();
        var sol = (await solTask.ConfigureAwait(false)).FirstOrDefault();
        if (pkg is null && sol is null) return null;

        var pkgTime = pkg?.StartedAtUtc ?? DateTime.MinValue;
        var solTime = sol?.StartedAtUtc ?? DateTime.MinValue;
        if (pkg is not null && (sol is null || pkgTime >= solTime))
        {
            return await BuildPackageAsync(conn.Client, pkg, logger).ConfigureAwait(false);
        }
        return await BuildSolutionAsync(conn.Client, sol!, includeFull, logger).ConfigureAwait(false);
    }

    private static async Task<DeploymentDetailResult> BuildPackageAsync(
        ServiceClient client,
        PackageHistoryRecord record,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var solReader = new SolutionHistoryReader(client, logger);
        IReadOnlyList<SolutionHistoryRecord> correlated = Array.Empty<SolutionHistoryRecord>();
        if (record.StartedAtUtc is { } startedAt)
        {
            try
            {
                if (record.CorrelationId is { } corrId && corrId != Guid.Empty)
                {
                    correlated = await solReader.GetByCorrelationIdAsync(corrId).ConfigureAwait(false);
                }
                if (correlated.Count == 0)
                {
                    var windowEnd = (record.CompletedAtUtc ?? startedAt) + CorrelationTailBuffer;
                    correlated = await solReader.GetInTimeWindowAsync(startedAt, windowEnd).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to enrich package run with solution history.");
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

        return new DeploymentDetailResult(
            Kind: DeploymentRunKind.Package,
            Package: record,
            CorrelatedSolutions: correlated,
            Solution: null,
            ParentPackage: null,
            ImportJobId: null,
            FormattedImportLog: null,
            AsyncOperation: null,
            Findings: findings);
    }

    private static async Task<DeploymentDetailResult> BuildSolutionAsync(
        ServiceClient client,
        SolutionHistoryRecord record,
        bool includeFull,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        PackageHistoryRecord? parentPackage = null;
        if (record.StartedAtUtc is { } startedAt)
        {
            try
            {
                var pkgReader = new PackageHistoryReader(client, logger);
                var nearby = await pkgReader.GetRecentAsync(50, startedAt - CorrelationTailBuffer, problemsOnly: false).ConfigureAwait(false);
                parentPackage = nearby.FirstOrDefault(p =>
                    p.StartedAtUtc is { } ps
                    && ps <= startedAt
                    && ((p.CompletedAtUtc ?? ps) + CorrelationTailBuffer) >= startedAt);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to locate parent package for solution history row.");
            }
        }

        string? formattedLog = null;
        ImportJobRecord? importJobMatch = null;
        if (includeFull)
        {
            try
            {
                var importReader = new ImportJobReader(client, logger);
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
                logger.LogDebug(ex, "Failed to retrieve formatted import log for solution history row.");
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

        return new DeploymentDetailResult(
            Kind: DeploymentRunKind.Solution,
            Package: null,
            CorrelatedSolutions: Array.Empty<SolutionHistoryRecord>(),
            Solution: record,
            ParentPackage: parentPackage,
            ImportJobId: importJobMatch?.Id,
            FormattedImportLog: formattedLog,
            AsyncOperation: null,
            Findings: findings);
    }

    private static async Task<DeploymentDetailResult?> TryBuildAsyncOperationAsync(
        ServiceClient client,
        Guid asyncOpId,
        bool includeFull,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        Entity entity;
        try
        {
            entity = await client.RetrieveAsync(
                DataverseSchema.AsyncOperation.EntityName,
                asyncOpId,
                new ColumnSet("statecode", "statuscode", "message", "friendlymessage", "createdon", "completedon"),
                default).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to retrieve asyncoperation {Id}.", asyncOpId);
            return null;
        }

        int statecode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
        int statuscode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;

        if (statecode != 3)
        {
            string stateLabel = statecode switch
            {
                0 => "Ready",
                1 => "Suspended",
                2 => "In Progress",
                _ => $"State {statecode}",
            };
            return new DeploymentDetailResult(
                Kind: DeploymentRunKind.AsyncOperationInProgress,
                Package: null,
                CorrelatedSolutions: Array.Empty<SolutionHistoryRecord>(),
                Solution: null,
                ParentPackage: null,
                ImportJobId: null,
                FormattedImportLog: null,
                AsyncOperation: new AsyncOperationSummary(asyncOpId, stateLabel, statecode, statuscode, Completed: false, Succeeded: false, Message: null),
                Findings: Array.Empty<string>());
        }

        bool succeeded = statuscode == 30;
        DateTime? completedOn = entity.Contains("completedon") ? entity.GetAttributeValue<DateTime>("completedon") : null;
        DateTime? createdOn = entity.Contains("createdon") ? entity.GetAttributeValue<DateTime>("createdon") : null;

        var historyReader = new SolutionHistoryReader(client, logger);
        DateTime pivot = completedOn ?? createdOn ?? DateTime.UtcNow;
        SolutionHistoryRecord? sol = null;
        bool veryRecent = (DateTime.UtcNow - pivot).TotalSeconds < 60;
        int attempts = veryRecent ? 3 : 1;
        for (int i = 0; i < attempts; i++)
        {
            if (i > 0) await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            sol = await historyReader.GetByActivityIdAsync(asyncOpId, nearUtc: pivot).ConfigureAwait(false);
            if (sol is not null) break;
        }
        if (sol is not null)
        {
            return await BuildSolutionAsync(client, sol, includeFull, logger).ConfigureAwait(false);
        }

        string? message = entity.GetAttributeValue<string>("friendlymessage")
            ?? entity.GetAttributeValue<string>("message");
        return new DeploymentDetailResult(
            Kind: DeploymentRunKind.AsyncOperationCompleted,
            Package: null,
            CorrelatedSolutions: Array.Empty<SolutionHistoryRecord>(),
            Solution: null,
            ParentPackage: null,
            ImportJobId: null,
            FormattedImportLog: null,
            AsyncOperation: new AsyncOperationSummary(asyncOpId, "Completed", statecode, statuscode, Completed: true, Succeeded: succeeded, Message: message),
            Findings: Array.Empty<string>());
    }

    private static bool IsNotFoundError(Exception ex)
    {
        if (ex.Message.Contains("0x80040217", StringComparison.OrdinalIgnoreCase)) return true;
        if (ex.Message.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase)) return true;
        if (ex.Message.Contains("ObjectDoesNotExist", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
