using TALXIS.CLI.Features.Environment.Solution;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Solution;

public class SolutionBuildOutputTests
{
    [Theory]
    [InlineData("  Error: RootComponent validation failed.", nameof(BuildOutputSeverity.Error))]
    [InlineData(@"C:\proj\proj.csproj(4,5): error MSB4018: task failed", nameof(BuildOutputSeverity.Error))]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.", nameof(BuildOutputSeverity.Error))]
    [InlineData("Following root components are not defined in customizations:", nameof(BuildOutputSeverity.Error))]
    [InlineData("  Following objects, required by the solution, are not present. ", nameof(BuildOutputSeverity.Error))]
    [InlineData("proj.csproj : warning NU1903: Package has a known vulnerability", nameof(BuildOutputSeverity.Warning))]
    [InlineData("  Warning: LocalBranchBuildVersionNumber is null", nameof(BuildOutputSeverity.Warning))]
    [InlineData("    0 Warning(s)", nameof(BuildOutputSeverity.Info))]
    [InlineData("    0 Error(s)", nameof(BuildOutputSeverity.Info))]
    [InlineData("src/App/index.ts(3,7): error TS2304: Cannot find name 'x'.", nameof(BuildOutputSeverity.Error))]
    [InlineData("npm error code E404", nameof(BuildOutputSeverity.Info))]
    [InlineData("Build succeeded.", nameof(BuildOutputSeverity.Info))]
    [InlineData("  Solution: bin\\Debug\\net462\\Sln.zip packed successfully", nameof(BuildOutputSeverity.Info))]
    public void Classify_ReturnsExpectedSeverity(string line, string expected)
    {
        Assert.Equal(Enum.Parse<BuildOutputSeverity>(expected), SolutionBuildOutput.Classify(line));
    }

    [Theory]
    [InlineData("{\n  \"Properties\": {\n    \"OutputPath\": \"bin\\\\Debug/net472/\"\n  }\n}", "bin\\Debug/net472/")]
    [InlineData("noise before\n{\"Properties\":{\"OutputPath\":\"bin/Release/net472/\"}}\nnoise after", "bin/Release/net472/")]
    public void ParseOutputPath_ReadsTheProperty(string output, string expected)
    {
        Assert.Equal(expected, SolutionBuildOutput.ParseOutputPath(output));
    }

    [Fact]
    public void ParseOutputPath_ReadsRealTwoPropertyOutput()
    {
        // Verbatim output of: dotnet build X.csproj -getProperty:OutputPath -getProperty:MSBuildProjectName
        const string output = "{\n  \"Properties\": {\n    \"OutputPath\": \"bin\\\\Debug/net472/\",\n    \"MSBuildProjectName\": \"Solutions.Test\"\n  }\n}\n";
        Assert.Equal("bin\\Debug/net472/", SolutionBuildOutput.ParseOutputPath(output));
    }

    [Fact]
    public void ParseOutputPath_ReturnsNull_ForSinglePropertyPlainText()
    {
        // What MSBuild prints for a single -getProperty: not JSON, so the command must request two.
        Assert.Null(SolutionBuildOutput.ParseOutputPath("bin\\Debug/net472/\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.")]
    [InlineData("{ not json }")]
    [InlineData("{\"Properties\":{}}")]
    [InlineData("{\"Properties\":{\"OutputPath\":\"\"}}")]
    public void ParseOutputPath_ReturnsNull_WhenMissingOrInvalid(string output)
    {
        Assert.Null(SolutionBuildOutput.ParseOutputPath(output));
    }

    [Theory]
    [InlineData("bin\\Debug/net472/")]
    [InlineData("bin/Debug/net472/")]
    [InlineData("bin\\Debug\\net472\\")]
    public void ResolveZipPath_NormalizesSeparators_AndUsesProjectName(string outputPath)
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "txc-proj");
        var csproj = Path.Combine(projectDir, "Solutions.DataModel.csproj");

        var zip = SolutionBuildOutput.ResolveZipPath(csproj, outputPath);

        Assert.Equal(Path.Combine(projectDir, "bin", "Debug", "net472", "Solutions.DataModel.zip"), zip);
    }
}
