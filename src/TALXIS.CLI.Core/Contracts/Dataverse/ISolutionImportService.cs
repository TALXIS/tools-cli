namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Information about a Dataverse solution's identity extracted from its ZIP
/// manifest or retrieved from the target environment.
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
/// Imports Dataverse solution .zip files into the environment referenced by
/// a profile. Encapsulates source-manifest parsing, existing-solution lookup,
/// import-path planning, and the actual import request/polling.
/// </summary>
public interface ISolutionImportService
{
    Task<SolutionImportResult> ImportAsync(
        string? profileName,
        string solutionZipPath,
        SolutionImportOptions options,
        CancellationToken ct);
}
