using System.Xml.Linq;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Environment;
using TALXIS.CLI.Environment.Platforms.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionImporterPathSelectionTests
{
    private static SolutionInfo Source(string version, bool managed) => new("publisherprefix_sample", new Version(version), managed);
    private static SolutionInfo Target(string version, bool managed) => new("publisherprefix_sample", new Version(version), managed);

    [Fact]
    public void SelectImportPath_ReturnsInstall_WhenTargetMissing()
    {
        var path = SolutionImporter.SelectImportPath(Source("1.0.0.0", managed: true), existingTarget: null, stageAndUpgrade: true);
        Assert.Equal(SolutionImportPath.Install, path);
    }

    [Fact]
    public void SelectImportPath_ReturnsUpdate_ForUnmanagedSource()
    {
        var path = SolutionImporter.SelectImportPath(Source("2.0.0.0", managed: false), Target("1.0.0.0", managed: true), stageAndUpgrade: true);
        Assert.Equal(SolutionImportPath.Update, path);
    }

    [Fact]
    public void SelectImportPath_ReturnsUpgrade_WhenManagedSourceHigherVersion()
    {
        var path = SolutionImporter.SelectImportPath(Source("2.0.0.0", managed: true), Target("1.5.0.0", managed: true), stageAndUpgrade: true);
        Assert.Equal(SolutionImportPath.Upgrade, path);
    }

    [Fact]
    public void SelectImportPath_ReturnsUpdate_WhenStageAndUpgradeDisabled()
    {
        var path = SolutionImporter.SelectImportPath(Source("2.0.0.0", managed: true), Target("1.5.0.0", managed: true), stageAndUpgrade: false);
        Assert.Equal(SolutionImportPath.Update, path);
    }

    [Fact]
    public void SelectImportPath_ReturnsUpdate_WhenManagedSourceSameOrLower()
    {
        var path = SolutionImporter.SelectImportPath(Source("1.5.0.0", managed: true), Target("1.5.0.0", managed: true), stageAndUpgrade: true);
        Assert.Equal(SolutionImportPath.Update, path);
    }

    [Fact]
    public void SmartDiffExpected_IsTrue_OnlyForUpgradeWithoutForceOverwrite()
    {
        Assert.True(SolutionImporter.SmartDiffExpected(SolutionImportPath.Upgrade, forceOverwrite: false));
        Assert.False(SolutionImporter.SmartDiffExpected(SolutionImportPath.Upgrade, forceOverwrite: true));
        Assert.False(SolutionImporter.SmartDiffExpected(SolutionImportPath.Update, forceOverwrite: false));
        Assert.False(SolutionImporter.SmartDiffExpected(SolutionImportPath.Install, forceOverwrite: false));
    }

    [Fact]
    public void ParseSolutionInfo_ReadsIdentityFromManifest()
    {
        var doc = XDocument.Parse("""
            <ImportExportXml>
              <SolutionManifest>
                <UniqueName>publisherprefix_sample</UniqueName>
                <Version>2.3.4.5</Version>
                <Managed>1</Managed>
              </SolutionManifest>
            </ImportExportXml>
            """);

        var info = SolutionImporter.ParseSolutionInfo(doc);

        Assert.Equal("publisherprefix_sample", info.UniqueName);
        Assert.Equal(new Version(2, 3, 4, 5), info.Version);
        Assert.True(info.Managed);
    }

    [Fact]
    public void ParseSolutionInfo_TreatsZeroManagedFlagAsUnmanaged()
    {
        var doc = XDocument.Parse("""
            <ImportExportXml>
              <SolutionManifest>
                <UniqueName>publisherprefix_sample</UniqueName>
                <Version>1.0.0.0</Version>
                <Managed>0</Managed>
              </SolutionManifest>
            </ImportExportXml>
            """);

        var info = SolutionImporter.ParseSolutionInfo(doc);

        Assert.False(info.Managed);
    }
}
