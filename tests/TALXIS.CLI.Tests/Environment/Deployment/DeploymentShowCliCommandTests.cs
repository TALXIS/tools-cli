using TALXIS.CLI.Features.Environment.Deployment;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Deployment;

/// <summary>
/// Validates that <see cref="DeploymentShowCliCommand"/> exposes exactly the
/// typed selectors the design mandates and that they are mutually exclusive.
///
/// The command rejects "nothing specified" and "more than one specified" via
/// inline validation at the top of <c>RunAsync</c> before any I/O is done, so
/// we can exercise the guard by invoking the command directly with no live
/// connection.
/// </summary>
public class DeploymentShowCliCommandTests
{
    [Fact]
    public async Task RunAsync_NoSelectorSpecified_ReturnsExitCode1()
    {
        var cmd = new DeploymentShowCliCommand();

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_MultipleSelectorsSpecified_ReturnsExitCode1()
    {
        var cmd = new DeploymentShowCliCommand
        {
            PackageRunId = Guid.NewGuid().ToString(),
            SolutionRunId = Guid.NewGuid().ToString(),
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_LatestAndNameTogether_ReturnsExitCode1()
    {
        var cmd = new DeploymentShowCliCommand
        {
            Latest = true,
            PackageName = "Some.Package",
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_InvalidPackageRunIdGuid_ReturnsExitCode1()
    {
        var cmd = new DeploymentShowCliCommand
        {
            PackageRunId = "not-a-guid",
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_EmptyPackageName_ReturnsExitCode1()
    {
        var cmd = new DeploymentShowCliCommand
        {
            PackageName = "   ",
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(1, exit);
    }
}
