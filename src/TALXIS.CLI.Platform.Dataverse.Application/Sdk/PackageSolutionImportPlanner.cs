using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Predicts which package solutions Package Deployer installs, updates or skips.
/// </summary>
public static class PackageSolutionImportPlanner
{
    public static IReadOnlyList<PackageSolutionImportPlan> Plan(
        IReadOnlyList<PackageSolutionInfo> packageSolutions,
        IReadOnlyDictionary<string, string?> installedVersions)
    {
        return packageSolutions
            .Select(s =>
            {
                var isInstalled = installedVersions.TryGetValue(s.UniqueName, out var installed);
                return new PackageSolutionImportPlan(s.UniqueName, s.Version, installed, Classify(s.Version, installed, isInstalled));
            })
            .ToList();
    }

    private static PackageSolutionImportStatus Classify(string? packageVersion, string? installedVersion, bool isInstalled)
    {
        if (!isInstalled) return PackageSolutionImportStatus.Install;
        if (!Version.TryParse(packageVersion, out var package) || !Version.TryParse(installedVersion, out var installed))
        {
            return PackageSolutionImportStatus.Unknown;
        }

        return package.CompareTo(installed) switch
        {
            > 0 => PackageSolutionImportStatus.Update,
            0 => PackageSolutionImportStatus.SkippedSameVersion,
            _ => PackageSolutionImportStatus.SkippedNewerInstalled,
        };
    }
}
