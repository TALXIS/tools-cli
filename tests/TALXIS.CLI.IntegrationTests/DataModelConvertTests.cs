using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Integration tests for <c>txc data model convert</c>.
/// Uses <see cref="TempWorkspaceFixture"/> to scaffold a real Power Platform solution
/// with an entity and attributes, then verifies conversion to each supported format.
/// </summary>
[Collection("Sequential")]
public class DataModelConvertTests : IClassFixture<TempWorkspaceFixture>
{
    private const string SkipReason = "Temporarily disabled: pp-solution template InitializeSolution.ps1 post-action is failing before test setup completes.";
    private readonly TempWorkspaceFixture _fixture;

    public DataModelConvertTests(TempWorkspaceFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory(Skip = SkipReason)]
    [InlineData("dbml", "table ")]
    [InlineData("sql", "CREATE TABLE")]
    [InlineData("edmx", "<edmx:Edmx")]
    [InlineData("ribbon", "RibbonDiffXml")]
    public async Task Convert_AllFormats_CreatesOutputFileWithExpectedContent(string format, string expectedContent)
    {
        var outputDir = Path.Combine(_fixture.TempDir, $"output-{format}");
        Directory.CreateDirectory(outputDir);

        await CliRunner.RunAsync(
            ["data", "model", "convert",
             "--input", _fixture.DeclarationsDir,
             "--target", format,
             "--output", outputDir]);

        var outputFile = Path.Combine(outputDir, $"solution.{format}");
        Assert.True(File.Exists(outputFile), $"Expected output file not found: {outputFile}");

        var content = await File.ReadAllTextAsync(outputFile);
        Assert.Contains(expectedContent, content);
    }

    [Fact(Skip = SkipReason)]
    public async Task Convert_DefaultOutput_WritesToExportsFolderAndUpdatesGitIgnore()
    {
        // Run from the solution dir so the default output resolves to <solutionDir>/exports/
        await CliRunner.RunAsync(
            ["data", "model", "convert",
             "--input", _fixture.DeclarationsDir,
             "--target", "dbml"],
            _fixture.SolutionDir);

        var exportsDir = Path.Combine(_fixture.SolutionDir, "exports");
        var outputFile = Path.Combine(exportsDir, "solution.dbml");

        Assert.True(Directory.Exists(exportsDir), "exports/ directory was not created");
        Assert.True(File.Exists(outputFile), $"Expected output file not found: {outputFile}");

        // Verify exports/ was added to the nearest .gitignore
        var gitIgnorePath = Path.Combine(_fixture.TempDir, ".gitignore");
        var gitIgnoreContent = await File.ReadAllTextAsync(gitIgnorePath);
        Assert.Contains("exports/", gitIgnoreContent);
    }

    [Fact(Skip = SkipReason)]
    public async Task Convert_DefaultInput_UsesCurrentDirectory()
    {
        var outputDir = Path.Combine(_fixture.TempDir, "output-default-input");
        Directory.CreateDirectory(outputDir);

        // Run from DeclarationsDir — omitting --input should pick up the current directory
        await CliRunner.RunAsync(
            ["data", "model", "convert",
             "--target", "dbml",
             "--output", outputDir],
            _fixture.DeclarationsDir);

        var outputFile = Path.Combine(outputDir, "solution.dbml");
        Assert.True(File.Exists(outputFile), $"Expected output file not found: {outputFile}");
    }

    [Fact(Skip = SkipReason)]
    public async Task Convert_InvalidTarget_ReturnsNonZeroExitCode()
    {
        var result = await CliRunner.RunRawAsync(
            ["data", "model", "convert",
             "--input", _fixture.DeclarationsDir,
             "--target", "invalid-format",
             "--output", _fixture.TempDir]);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact(Skip = SkipReason)]
    public async Task Convert_NonExistentInput_ReturnsNonZeroExitCode()
    {
        var result = await CliRunner.RunRawAsync(
            ["data", "model", "convert",
             "--input", "/nonexistent/path/that/does/not/exist",
             "--target", "dbml",
             "--output", _fixture.TempDir]);

        Assert.NotEqual(0, result.ExitCode);
    }
}
