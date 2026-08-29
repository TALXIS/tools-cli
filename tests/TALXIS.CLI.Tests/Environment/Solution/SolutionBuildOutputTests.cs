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
    [InlineData("Build succeeded.", nameof(BuildOutputSeverity.Info))]
    [InlineData("  Solution: bin\\Debug\\net462\\Sln.zip packed successfully", nameof(BuildOutputSeverity.Info))]
    public void Classify_ReturnsExpectedSeverity(string line, string expected)
    {
        Assert.Equal(Enum.Parse<BuildOutputSeverity>(expected), SolutionBuildOutput.Classify(line));
    }

    [Fact]
    public void FindZips_ReturnsEmpty_WhenDirectoryMissing()
    {
        var (fresh, all) = SolutionBuildOutput.FindSolutionZips(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), DateTime.UtcNow);
        Assert.Empty(fresh);
        Assert.Empty(all);
    }

    [Fact]
    public void FindZips_FindsZipInAnyTfmSubfolder()
    {
        using var dir = new TempDir();
        var zip = dir.CreateFile(Path.Combine("net472", "Sln.zip"));

        var (fresh, all) = SolutionBuildOutput.FindSolutionZips(dir.Path, DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal([zip], fresh);
        Assert.Equal([zip], all);
    }

    [Fact]
    public void FindZips_ExcludesStaleZipsFromFresh()
    {
        using var dir = new TempDir();
        var stale = dir.CreateFile(Path.Combine("net462", "Old.zip"));
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        var (fresh, all) = SolutionBuildOutput.FindSolutionZips(dir.Path, DateTime.UtcNow.AddMinutes(-1));

        Assert.Empty(fresh);
        Assert.Equal([stale], all);
    }

    [Fact]
    public void FindZips_OrdersFreshNewestFirst()
    {
        using var dir = new TempDir();
        var older = dir.CreateFile(Path.Combine("net462", "Older.zip"));
        var newer = dir.CreateFile(Path.Combine("net472", "Newer.zip"));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddSeconds(-30));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var (fresh, _) = SolutionBuildOutput.FindSolutionZips(dir.Path, DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal([newer, older], fresh);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "txc-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string CreateFile(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, [0x50, 0x4B]);
            return fullPath;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
