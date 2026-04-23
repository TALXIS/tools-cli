using TALXIS.CLI.Environment;
using TALXIS.CLI.Environment.Package;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Package;

public class PackageUninstallCliCommandTests
{
    [Fact]
    public void BuildReverseUninstallOrderFromImportConfig_ReversesGivenImportOrder()
    {
        var importOrder = new[] { "base", "mid", "top" };

        var order = PackageUninstallCliCommand.BuildReverseUninstallOrderFromImportConfig(importOrder);

        Assert.Equal(new[] { "top", "mid", "base" }, order);
    }

    [Fact]
    public void BuildReverseUninstallOrderFromImportConfig_DeduplicatesCaseInsensitive()
    {
        var importOrder = new[] { "Base", "Top", "base" };

        var order = PackageUninstallCliCommand.BuildReverseUninstallOrderFromImportConfig(importOrder);

        Assert.Equal(new[] { "Top", "Base" }, order);
    }
}
