namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// What Package Deployer does with a package solution, given the version installed in the target environment.
/// </summary>
public enum PackageSolutionImportStatus
{
    Install = 0,
    Update = 1,
    SkippedSameVersion = 2,
    SkippedNewerInstalled = 3,
    Unknown = 4,
}
