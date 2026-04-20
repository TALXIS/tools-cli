using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Information about a Dataverse solution's identity extracted from its ZIP manifest
/// or retrieved from the target environment.
/// </summary>
public sealed record SolutionInfo(string UniqueName, Version Version, bool Managed);

public enum SolutionImportPath
{
    /// <summary>Target environment has no solution with this unique name.</summary>
    Install,
    /// <summary>Plain import over an existing solution (unmanaged, or managed without single-step upgrade).</summary>
    Update,
    /// <summary>Single-step upgrade (<c>StageAndUpgradeRequest</c>) over an existing managed solution.</summary>
    Upgrade,
}

public sealed record SolutionImportOptions(
    bool StageAndUpgrade,
    bool ForceOverwrite,
    bool PublishWorkflows,
    bool SkipDependencyCheck,
    bool SkipLowerVersion,
    bool Async);

public sealed record SolutionImportResult(
    SolutionImportPath Path,
    SolutionInfo Source,
    SolutionInfo? ExistingTarget,
    Guid ImportJobId,
    Guid? AsyncOperationId,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool SmartDiffExpected,
    string Status);

/// <summary>
/// Orchestrates solution imports against Dataverse via the modern <see cref="ServiceClient"/>.
/// Responsibilities: read solution metadata, compare with target, stage, import, and poll.
/// </summary>
public sealed class SolutionImporter
{
    private readonly IOrganizationServiceAsync2 _service;
    private readonly ILogger? _logger;

    public SolutionImporter(IOrganizationServiceAsync2 service, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    /// <summary>
    /// Reads <c>solution.xml</c> from the root of a solution ZIP and returns its identity.
    /// </summary>
    public static SolutionInfo ReadSolutionInfo(string solutionZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionZipPath);

        if (!File.Exists(solutionZipPath))
        {
            throw new FileNotFoundException($"Solution zip not found: {solutionZipPath}", solutionZipPath);
        }

        using var archive = ZipFile.OpenRead(solutionZipPath);
        var entry = archive.GetEntry("solution.xml")
            ?? throw new InvalidOperationException($"solution.xml not found in {solutionZipPath}.");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        return ParseSolutionInfo(doc);
    }

    public static SolutionInfo ParseSolutionInfo(XDocument doc)
    {
        var manifest = doc.Root?.Element("SolutionManifest")
            ?? throw new InvalidOperationException("SolutionManifest missing from solution.xml.");

        string uniqueName = manifest.Element("UniqueName")?.Value?.Trim()
            ?? throw new InvalidOperationException("UniqueName missing from solution.xml.");

        string versionText = manifest.Element("Version")?.Value?.Trim()
            ?? throw new InvalidOperationException("Version missing from solution.xml.");

        string managedText = manifest.Element("Managed")?.Value?.Trim() ?? "0";

        if (!Version.TryParse(versionText, out var version))
        {
            throw new InvalidOperationException($"Unable to parse solution version '{versionText}'.");
        }

        bool managed = managedText == "1" || string.Equals(managedText, "true", StringComparison.OrdinalIgnoreCase);
        return new SolutionInfo(uniqueName, version, managed);
    }

    /// <summary>
    /// Deterministic import-path selection. Exposed for unit tests so the heuristic stays honest
    /// without hitting a live Dataverse environment.
    /// </summary>
    public static SolutionImportPath SelectImportPath(SolutionInfo source, SolutionInfo? existingTarget, bool stageAndUpgrade)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (existingTarget is null)
        {
            return SolutionImportPath.Install;
        }

        // Unmanaged imports never take the upgrade path.
        if (!source.Managed)
        {
            return SolutionImportPath.Update;
        }

        if (stageAndUpgrade && source.Version > existingTarget.Version)
        {
            return SolutionImportPath.Upgrade;
        }

        return SolutionImportPath.Update;
    }

    /// <summary>
    /// Whether SmartDiff is expected to apply for the chosen path + options. SmartDiff only
    /// applies on the upgrade path when force-overwrite is off.
    /// </summary>
    public static bool SmartDiffExpected(SolutionImportPath path, bool forceOverwrite)
        => path == SolutionImportPath.Upgrade && !forceOverwrite;

    public async Task<SolutionInfo?> GetExistingSolutionAsync(string uniqueName, CancellationToken cancellationToken = default)
    {
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("uniquename", "version", "ismanaged"),
            Criteria = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);

        var result = await _service.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
        if (result.Entities.Count == 0)
        {
            return null;
        }

        var entity = result.Entities[0];
        string version = entity.GetAttributeValue<string>("version") ?? "0.0.0.0";
        bool managed = entity.GetAttributeValue<bool>("ismanaged");
        if (!Version.TryParse(version, out var parsedVersion))
        {
            parsedVersion = new Version(0, 0, 0, 0);
        }

        return new SolutionInfo(uniqueName, parsedVersion, managed);
    }

    public async Task<SolutionImportResult> ImportAsync(
        string solutionZipPath,
        SolutionImportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var source = ReadSolutionInfo(solutionZipPath);
        var existing = await GetExistingSolutionAsync(source.UniqueName, cancellationToken).ConfigureAwait(false);

        if (options.SkipLowerVersion && existing is not null && source.Version <= existing.Version)
        {
            return new SolutionImportResult(
                existing.Managed ? SolutionImportPath.Update : SolutionImportPath.Update,
                source,
                existing,
                Guid.Empty,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow,
                SmartDiffExpected: false,
                Status: "Skipped (source version not higher than target; --skip-lower-version).");
        }

        var path = SelectImportPath(source, existing, options.StageAndUpgrade);
        bool smartDiffExpected = SmartDiffExpected(path, options.ForceOverwrite);

        byte[] customizationFile = await File.ReadAllBytesAsync(solutionZipPath, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Staging solution {UniqueName} {Version}...", source.UniqueName, source.Version);

        var stageRequest = new StageSolutionRequest
        {
            CustomizationFile = customizationFile
        };

        var stageResponse = (StageSolutionResponse)await _service
            .ExecuteAsync(stageRequest, cancellationToken)
            .ConfigureAwait(false);

        var stageResults = stageResponse.StageSolutionResults
            ?? throw new InvalidOperationException("StageSolutionResponse did not include staging results.");

        if (stageResults.StageSolutionStatus != StageSolutionStatus.Passed)
        {
            string messages = string.Join("; ", stageResults.SolutionValidationResults?
                .Select(v => $"{v.SolutionValidationResultType}: {v.Message}") ?? Array.Empty<string>());
            throw new InvalidOperationException(
                $"Solution staging failed ({stageResults.StageSolutionStatus}). {messages}");
        }

        DateTime startedAtUtc = DateTime.UtcNow;
        Guid importJobId = Guid.NewGuid();
        Guid? asyncOperationId = null;
        string status;

        if (path == SolutionImportPath.Upgrade)
        {
            var stageAndUpgrade = new StageAndUpgradeAsyncRequest
            {
                CustomizationFile = customizationFile,
                OverwriteUnmanagedCustomizations = options.ForceOverwrite,
                PublishWorkflows = options.PublishWorkflows,
                SkipProductUpdateDependencies = options.SkipDependencyCheck,
                ImportJobId = importJobId
            };

            var response = (StageAndUpgradeAsyncResponse)await _service
                .ExecuteAsync(stageAndUpgrade, cancellationToken)
                .ConfigureAwait(false);

            asyncOperationId = response.AsyncOperationId;
            status = options.Async
                ? "Queued (async)"
                : await PollAsyncOperationAsync(response.AsyncOperationId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var import = new ImportSolutionAsyncRequest
            {
                CustomizationFile = customizationFile,
                OverwriteUnmanagedCustomizations = options.ForceOverwrite,
                PublishWorkflows = options.PublishWorkflows,
                SkipProductUpdateDependencies = options.SkipDependencyCheck,
                ImportJobId = importJobId
            };

            var response = (ImportSolutionAsyncResponse)await _service
                .ExecuteAsync(import, cancellationToken)
                .ConfigureAwait(false);

            asyncOperationId = response.AsyncOperationId;
            status = options.Async
                ? "Queued (async)"
                : await PollAsyncOperationAsync(response.AsyncOperationId, cancellationToken).ConfigureAwait(false);
        }

        DateTime? completedAtUtc = options.Async ? null : DateTime.UtcNow;

        return new SolutionImportResult(
            path,
            source,
            existing,
            importJobId,
            asyncOperationId,
            startedAtUtc,
            completedAtUtc,
            smartDiffExpected,
            status);
    }

    private async Task<string> PollAsyncOperationAsync(Guid asyncOperationId, CancellationToken cancellationToken)
    {
        // Poll asyncoperation row until state transitions to Completed. StateCode values:
        // 0 = Ready, 1 = Suspended, 2 = Locked, 3 = Completed.
        var delay = TimeSpan.FromSeconds(3);
        while (!cancellationToken.IsCancellationRequested)
        {
            var entity = await _service.RetrieveAsync(
                "asyncoperation",
                asyncOperationId,
                new ColumnSet("statecode", "statuscode", "message", "friendlymessage"),
                cancellationToken).ConfigureAwait(false);

            var state = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
            if (state == 3) // Completed
            {
                var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;
                // 30 = Succeeded; everything else indicates failure/cancellation.
                if (statusCode == 30)
                {
                    return "Succeeded";
                }

                string message = entity.GetAttributeValue<string>("friendlymessage")
                    ?? entity.GetAttributeValue<string>("message")
                    ?? $"status code {statusCode}";
                throw new InvalidOperationException($"Solution import failed: {message}");
            }

            _logger?.LogDebug("Waiting for async operation {Id}...", asyncOperationId);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return "Cancelled";
    }
}
