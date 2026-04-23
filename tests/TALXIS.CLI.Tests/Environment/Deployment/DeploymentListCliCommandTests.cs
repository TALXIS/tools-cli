using TALXIS.CLI.Core.Platforms.Dataverse;
using TALXIS.CLI.Platform.Dataverse;
using TALXIS.CLI.Features.Environment;
using TALXIS.CLI.Features.Environment.Deployment;
using TALXIS.CLI.Platform.Dataverse.Platforms;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Deployment;

public class DeploymentListCliCommandTests
{
    [Fact]
    public void BuildRows_InterleavesBothStreamsByStartTimeDesc()
    {
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);

        var packages = new[]
        {
            new PackageHistoryRecord(Guid.NewGuid(), "pkg-older", "Success", "Completed", older, older.AddMinutes(5), null, null),
        };
        var solutions = new[]
        {
            new SolutionHistoryRecord(Guid.NewGuid(), "sol-newer", "1.0.0.0", null, 1, "Import", 1, "Install", null, newer, newer.AddMinutes(3), "Completed"),
        };

        var rows = DeploymentListCliCommand.BuildRows(packages, solutions);

        Assert.Equal(2, rows.Count);
        Assert.Equal("sol", rows[0].Kind);
        Assert.Equal("sol-newer", rows[0].Name);
        Assert.Equal("pkg", rows[1].Kind);
    }

    [Theory]
    [InlineData("Success", "OK")]
    [InlineData("Completed", "OK")]
    [InlineData("Failed", "FAILED")]
    [InlineData("In Process", "IN PROGRESS")]
    [InlineData(null, "UNKNOWN")]
    public void BuildRows_NormalizesPackageStatusLabels(string? raw, string expected)
    {
        var started = DateTime.UtcNow;
        var packages = new[]
        {
            new PackageHistoryRecord(Guid.NewGuid(), "p", raw, null, started, null, null, null),
        };
        var rows = DeploymentListCliCommand.BuildRows(packages, Array.Empty<SolutionHistoryRecord>());
        Assert.Equal(expected, rows[0].Status);
    }
}
