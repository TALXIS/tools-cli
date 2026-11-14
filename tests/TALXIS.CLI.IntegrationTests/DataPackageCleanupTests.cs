using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Black-box exit-code tests for <c>txc data pkg cleanup</c>. These don't
/// connect to a live environment — they just confirm the command is wired
/// into the tree and rejects obviously bad inputs at the validation stage.
/// </summary>
[Collection("Sequential")]
public class DataPackageCleanupTests
{
    [Fact]
    public async Task Cleanup_Help_ExitsZero()
    {
        var result = await CliRunner.RunRawAsync(["data", "pkg", "cleanup", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("cleanup", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--yes", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--missing-action", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cleanup_NonExistentPath_ReturnsValidationError()
    {
        var result = await CliRunner.RunRawAsync(
            ["data", "pkg", "cleanup",
             Path.Combine(Path.GetTempPath(), "txc-no-such-package-" + Guid.NewGuid().ToString("N")),
             "--yes"]);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Cleanup_UnknownMissingAction_ReturnsValidationError()
    {
        var folder = Path.Combine(Path.GetTempPath(), "txc-cleanup-bad-flag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "data_schema.xml"), "<entities></entities>");
            File.WriteAllText(Path.Combine(folder, "data.xml"), "<entities></entities>");

            var result = await CliRunner.RunRawAsync(
                ["data", "pkg", "cleanup", folder,
                 "--missing-action", "magic",
                 "--yes"]);

            Assert.NotEqual(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
