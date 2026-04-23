using TALXIS.CLI.Config.Platforms.Dataverse;
using System.Text.RegularExpressions;

namespace TALXIS.CLI.Platform.Dataverse.Platforms;

/// <summary>
/// Input to <see cref="DeploymentFindingsAnalyzer"/>. Carries only structured records already
/// fetched by the readers; the analyzer never calls Dataverse.
/// </summary>
public sealed record DeploymentFindingsInput
{
    /// <summary>Raw <c>importjob.data</c> XML for the run under inspection (solution mode). Null in package mode.</summary>
    public string? ImportJobData { get; init; }

    /// <summary>The primary solution-history row for the run (solution mode). Null when not resolved.</summary>
    public SolutionHistoryRecord? Primary { get; init; }

    /// <summary>All correlated solution-history rows (solution or package mode).</summary>
    public IReadOnlyList<SolutionHistoryRecord> Solutions { get; init; } = Array.Empty<SolutionHistoryRecord>();

    /// <summary>True when running in package mode (selectors for a package history row).</summary>
    public bool IsPackageMode { get; init; }

    /// <summary>True when the caller requested or produced per-solution correlation data.</summary>
    public bool IncludeSolutions { get; init; }

    /// <summary>Status string from the <c>packagehistory</c> row (package mode only). Used for stale-in-process detection.</summary>
    public string? PackageStatus { get; init; }

    /// <summary>UTC start time from the <c>packagehistory</c> row (package mode only).</summary>
    public DateTime? PackageStartedAtUtc { get; init; }
}

/// <summary>
/// Pure analyzer that turns already-fetched deployment records into actionable
/// finding strings ("what happened / why / what to change") for <c>txc environment deployment show</c>.
/// The exact phrasing of every string is locked in by the plan.
/// </summary>
public static class DeploymentFindingsAnalyzer
{
    private const string OverwriteFinding = "Overwrite customizations was enabled — SmartDiff was skipped. Re-run without --force-overwrite to cut import time.";
    private const string InstallUpgradeFinding = "Run used install + upgrade pattern instead of a single-step upgrade. Use --stage-and-upgrade to keep the upgrade atomic.";
    private const string InstallUpdateFinding = "One or more solutions were installed and then updated within the same deployment window — this may indicate redundant imports or overlapping deployments.";
    private const string SmartDiffAbsentFinding = "SmartDiff did not apply on upgrade path. Check settings or component churn.";
    private const string StaleInProcessFinding = "Package history status is still 'In Process' — the deployment host may have been interrupted before recording the final status. Verify all correlated solutions completed successfully.";
    private const string NoSolutionsDetectedFinding = "No solution imports were detected for this package run — all solutions were likely already at the required version and skipped by Package Deployer.";

    public static IReadOnlyList<string> Analyze(DeploymentFindingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var findings = new List<string>();

        TryEmitStaleInProcess(input, findings);
        TryEmitNoSolutionsDetected(input, findings);
        bool overwriteFired = TryEmitOverwrite(input, findings);
        TryEmitInstallUpgradePattern(input, findings);
        TryEmitSmartDiffAbsent(input, overwriteFired, findings);
        TryEmitSlowestImports(input, findings);

        return findings;
    }

    private static void TryEmitStaleInProcess(DeploymentFindingsInput input, List<string> findings)
    {
        // Only applies in package mode where we have a packagehistory row.
        // If the status string contains "In Process" (case-insensitive) and the record is older
        // than 1 hour, the deployment host was likely interrupted before writing the final status.
        if (!input.IsPackageMode || input.PackageStatus is null)
        {
            return;
        }

        if (!input.PackageStatus.Contains("In Process", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var startedAt = input.PackageStartedAtUtc;
        if (startedAt is null || (DateTime.UtcNow - startedAt.Value).TotalHours < 1)
        {
            return;
        }

        findings.Add(StaleInProcessFinding);
    }

    private static void TryEmitNoSolutionsDetected(DeploymentFindingsInput input, List<string> findings)
    {
        // Only meaningful in package mode when we actively requested solution correlation.
        // If no solution-history records were found AND the package completed, it is very likely
        // PD determined all solutions were already at the required version and skipped imports.
        if (!input.IsPackageMode || !input.IncludeSolutions)
        {
            return;
        }

        if (input.Solutions.Count != 0)
        {
            return;
        }

        // Only emit when the package actually completed — stale "In Process" already has its own finding.
        if (input.PackageStatus is null
            || (!input.PackageStatus.Contains("Completed", StringComparison.OrdinalIgnoreCase)
                && !input.PackageStatus.Contains("Success", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        findings.Add(NoSolutionsDetectedFinding);
    }

    private static bool TryEmitOverwrite(DeploymentFindingsInput input, List<string> findings)
    {
        // Evidence: explicit boolean on the solution-history row, or the overwriteunmanagedcustomizations
        // attribute in the importjob.data XML (stamped when --force-overwrite is passed).
        bool overwriteFromRecord = input.Primary?.OverwriteUnmanagedCustomizations == true;
        bool overwriteFromXml = ContainsOverwriteUnmanagedCustomizations(input.ImportJobData);
        if (!overwriteFromRecord && !overwriteFromXml)
        {
            return false;
        }

        int? sub = input.Primary?.SuboperationCode;
        if (sub != 3 && sub != 5)
        {
            return false;
        }

        // "managed target" qualifier: Upgrade (5) is always a managed-only concept.
        // For Update (3), we rely on the XML Managed marker; if absent we skip (tolerant).
        if (sub == 3 && !IndicatesManagedTarget(input.ImportJobData))
        {
            return false;
        }

        findings.Add(OverwriteFinding);
        return true;
    }

    private static void TryEmitInstallUpgradePattern(DeploymentFindingsInput input, List<string> findings)
    {
        if (input.Solutions.Count < 2)
        {
            return;
        }

        // Group by solution unique name; check for two distinct anti-patterns:
        // 1. Install/Holding (1|2) + Upgrade (5): two-step holding pattern instead of single-step upgrade.
        // 2. Install (1) + Update (3): solution was installed then updated in the same window,
        //    which may indicate redundant imports or overlapping deployments.
        var groups = input.Solutions
            .Where(s => !string.IsNullOrWhiteSpace(s.SolutionName))
            .GroupBy(s => s.SolutionName!, StringComparer.OrdinalIgnoreCase);

        bool installUpgradeFired = false;
        bool installUpdateFired = false;

        foreach (var g in groups)
        {
            bool hasInstallOrHolding = g.Any(s => s.SuboperationCode is 1 or 2);
            bool hasUpgrade = g.Any(s => s.SuboperationCode == 5);
            bool hasUpdate = g.Any(s => s.SuboperationCode == 3);

            if (!installUpgradeFired && hasInstallOrHolding && hasUpgrade)
            {
                findings.Add(InstallUpgradeFinding);
                installUpgradeFired = true;
            }

            if (!installUpdateFired && hasInstallOrHolding && hasUpdate)
            {
                findings.Add(InstallUpdateFinding);
                installUpdateFired = true;
            }

            if (installUpgradeFired && installUpdateFired)
            {
                return;
            }
        }
    }

    private static void TryEmitSmartDiffAbsent(DeploymentFindingsInput input, bool overwriteFired, List<string> findings)
    {
        if (overwriteFired)
        {
            return;
        }

        if (input.Primary?.SuboperationCode != 5)
        {
            return;
        }

        // Need XML evidence — if the XML is missing we can't tell and stay quiet (tolerant).
        if (string.IsNullOrWhiteSpace(input.ImportJobData))
        {
            return;
        }

        if (ContainsSmartDiffIndicator(input.ImportJobData))
        {
            return;
        }

        findings.Add(SmartDiffAbsentFinding);
    }

    private static void TryEmitSlowestImports(DeploymentFindingsInput input, List<string> findings)
    {
        if (!input.IsPackageMode || !input.IncludeSolutions)
        {
            return;
        }

        var withDuration = input.Solutions
            .Where(s => !string.IsNullOrWhiteSpace(s.SolutionName)
                && s.StartedAtUtc is not null
                && s.CompletedAtUtc is not null)
            .Select(s => new { s.SolutionName, Duration = s.CompletedAtUtc!.Value - s.StartedAtUtc!.Value })
            .OrderByDescending(x => x.Duration)
            .ToList();

        if (withDuration.Count < 2)
        {
            return;
        }

        var top = withDuration.Take(2).ToList();
        findings.Add($"Slowest imports: {top[0].SolutionName}, {top[1].SolutionName} — primary candidates for optimization.");
    }

    // --- Tolerant XML-ish scans ---
    // We intentionally avoid full XML parsing: the `data` payload is a large, noisy document
    // whose schema has drifted across Dataverse releases. A substring/regex scan is good enough
    // for "is this attribute/element present at all?" questions, and it cannot throw.

    internal static bool ContainsOverwriteUnmanagedCustomizations(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return false;
        try
        {
            // Accept the attribute-on-root form used by Dataverse.
            return Regex.IsMatch(xml, @"overwriteunmanagedcustomizations\s*=\s*""\s*(?:1|true)\s*""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IndicatesManagedTarget(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return false;
        try
        {
            // Solutions export with Managed="1" attribute when managed.
            return Regex.IsMatch(xml, @"\bManaged\s*=\s*""\s*1\s*""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }

    internal static bool ContainsSmartDiffIndicator(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return false;
        try
        {
            // Either a <SmartDiff> element or a smartDiff attribute anywhere in the payload.
            return Regex.IsMatch(xml, @"<\s*SmartDiff\b|\bsmartDiff\s*=",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }
}
