using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Environment.Deployment;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Deployment;

/// <summary>
/// Validates that <see cref="DeploymentGetCliCommand"/> exposes exactly the
/// typed selectors the design mandates and that they are mutually exclusive.
///
/// The command rejects "nothing specified" and "more than one specified" via
/// inline validation at the top of <c>ExecuteAsync</c> before any I/O is done,
/// so we can exercise the guard by invoking the command directly with no live
/// connection. Validation errors return <see cref="TxcLeafCommand.ExitValidationError"/> (2).
/// </summary>
public class DeploymentGetCliCommandTests
{
    private const int ExitValidationError = 2;

    [Fact]
    public async Task RunAsync_NoSelectorSpecified_ReturnsValidationError()
    {
        var cmd = new DeploymentGetCliCommand();

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }

    [Fact]
    public async Task RunAsync_MultipleSelectorsSpecified_ReturnsValidationError()
    {
        var cmd = new DeploymentGetCliCommand
        {
            PackageRunId = Guid.NewGuid().ToString(),
            SolutionRunId = Guid.NewGuid().ToString(),
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }

    [Fact]
    public async Task RunAsync_LatestAndNameTogether_ReturnsValidationError()
    {
        var cmd = new DeploymentGetCliCommand
        {
            Latest = true,
            PackageName = "Some.Package",
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }

    [Fact]
    public async Task RunAsync_InvalidPackageRunIdGuid_ReturnsValidationError()
    {
        var cmd = new DeploymentGetCliCommand
        {
            PackageRunId = "not-a-guid",
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }

    [Fact]
    public async Task RunAsync_EmptyPackageName_ReturnsValidationError()
    {
        var cmd = new DeploymentGetCliCommand
        {
            PackageName = "   ",
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }
}
