namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record PackageUninstallRequest(
    string? ProfileName,
    string PackageSource,
    string PackageVersion,
    string? OutputDirectory);

public sealed record PackageUninstallResult(
    string PackageDisplayName,
    IReadOnlyList<string> UninstallOrder,
    IReadOnlyList<SolutionUninstallOutcome> Outcomes);

/// <summary>
/// Provider-agnostic service for uninstalling all solutions of a deployable package from the
/// target environment in reverse import order. Owns package-source reading, solution
/// orchestration, and history-record lifecycle.
/// </summary>
public interface IPackageUninstallService
{
    Task<PackageUninstallResult> UninstallAsync(PackageUninstallRequest request, CancellationToken ct);
}
