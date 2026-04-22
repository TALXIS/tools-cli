using TALXIS.CLI.Deploy;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy;

public class DeployCommandValidationTests
{
    [Fact]
    public async Task DeployRun_InvalidType_ReturnsError()
    {
        var cmd = new DeployRunCliCommand
        {
            Type = "unknown",
            Source = "anything",
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployRun_PackageWithSolutionFlags_ReturnsError()
    {
        var cmd = new DeployRunCliCommand
        {
            Type = "package",
            Source = "TALXIS.Controls.FileExplorer.Package",
            ForceOverwrite = true,
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployList_InvalidResource_ReturnsError()
    {
        var cmd = new DeployListCliCommand
        {
            Resource = "invalid",
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployList_RunsWithManagedFilter_ReturnsError()
    {
        var cmd = new DeployListCliCommand
        {
            Resource = "runs",
            Managed = "true",
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployUninstall_WithoutYes_ReturnsError()
    {
        var cmd = new DeployUninstallCliCommand
        {
            SolutionName = "MySolution",
            Yes = false,
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployUninstall_WithoutSelector_ReturnsError()
    {
        var cmd = new DeployUninstallCliCommand
        {
            Yes = true,
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployUninstall_InvalidPackageRunId_ReturnsError()
    {
        var cmd = new DeployUninstallCliCommand
        {
            PackageName = "pkg",
            PackageRunId = "not-a-guid",
            Yes = true,
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DeployUninstall_PackageRunIdWithoutPackageName_ReturnsError()
    {
        var cmd = new DeployUninstallCliCommand
        {
            PackageRunId = Guid.NewGuid().ToString(),
            Yes = true,
        };

        var exitCode = await cmd.RunAsync();
        Assert.Equal(1, exitCode);
    }
}
