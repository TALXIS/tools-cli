using System.IO.Compression;
using System.Text;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class ControlVersionCheckerTests
{
    private static readonly PackagedControl Grid = new("talxis_TALXIS.PCF.Grid", "0.0.3");

    [Fact]
    public void FindNotNewer_Warns_WhenPackagedVersionIsLower()
    {
        var installed = new Dictionary<string, string> { [Grid.Name] = "0.0.99008" };

        var warning = Assert.Single(ControlVersionChecker.FindNotNewer([Grid], installed));

        Assert.Equal(Grid.Name, warning.ControlName);
        Assert.Equal("0.0.3", warning.PackagedVersion);
        Assert.Equal("0.0.99008", warning.InstalledVersion);
    }

    [Fact]
    public void FindNotNewer_Warns_WhenPackagedVersionIsEqual()
    {
        var installed = new Dictionary<string, string> { [Grid.Name] = "0.0.3" };

        Assert.Single(ControlVersionChecker.FindNotNewer([Grid], installed));
    }

    [Fact]
    public void FindNotNewer_IsSilent_WhenPackagedVersionIsHigherOrControlIsNew()
    {
        var higher = new Dictionary<string, string> { [Grid.Name] = "0.0.2" };

        Assert.Empty(ControlVersionChecker.FindNotNewer([Grid], higher));
        Assert.Empty(ControlVersionChecker.FindNotNewer([Grid], new Dictionary<string, string>()));
    }

    [Fact]
    public void FindNotApplied_Warns_WhenInstalledVersionDiffersAfterImport()
    {
        var after = new Dictionary<string, string> { [Grid.Name] = "0.0.2" };

        Assert.Single(ControlVersionChecker.FindNotApplied([Grid], after));
        Assert.Empty(ControlVersionChecker.FindNotApplied([Grid], new Dictionary<string, string> { [Grid.Name] = "0.0.3" }));
    }

    [Fact]
    public void ReadPackagedControls_ReadsNameFromFolderAndVersionFromManifest()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "solution.xml", "<ImportExportXml />");
            AddEntry(archive, "Controls/talxis_TALXIS.PCF.Grid/ControlManifest.xml",
                "<manifest><control namespace=\"TALXIS.PCF\" constructor=\"Grid\" version=\"0.0.3\" /></manifest>");
        }

        buffer.Position = 0;
        using var read = new ZipArchive(buffer, ZipArchiveMode.Read);

        var control = Assert.Single(ControlVersionChecker.ReadPackagedControls(read));
        Assert.Equal(Grid, control);
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
