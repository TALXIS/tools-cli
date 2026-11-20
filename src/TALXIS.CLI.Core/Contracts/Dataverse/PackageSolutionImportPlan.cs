namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// One package solution compared with the version installed in the target environment.
/// </summary>
public sealed record PackageSolutionImportPlan(
    string UniqueName,
    string? PackageVersion,
    string? InstalledVersion,
    PackageSolutionImportStatus Status);
