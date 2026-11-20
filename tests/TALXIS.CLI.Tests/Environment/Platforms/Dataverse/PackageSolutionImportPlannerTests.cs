using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class PackageSolutionImportPlannerTests
{
    [Theory]
    [InlineData("1.0.0.0", null, false, PackageSolutionImportStatus.Install)]
    [InlineData("1.1.0.0", "1.0.0.0", true, PackageSolutionImportStatus.Update)]
    [InlineData("0.0.20000.0", "0.0.20000.0", true, PackageSolutionImportStatus.SkippedSameVersion)]
    [InlineData("1.0.0.0", "2.0.0.0", true, PackageSolutionImportStatus.SkippedNewerInstalled)]
    [InlineData("not-a-version", "1.0.0.0", true, PackageSolutionImportStatus.Unknown)]
    public void ComparesPackageVersionWithInstalledVersion(string packageVersion, string? installedVersion, bool isInstalled, PackageSolutionImportStatus expected)
    {
        var installed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (isInstalled) installed["Repro_Solution"] = installedVersion;

        var plan = PackageSolutionImportPlanner.Plan(new[] { new PackageSolutionInfo("repro_solution", packageVersion) }, installed);

        var row = Assert.Single(plan);
        Assert.Equal(expected, row.Status);
        Assert.Equal(installedVersion, row.InstalledVersion);
    }
}
