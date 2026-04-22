using TALXIS.CLI.Deploy;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy;

public class DeployUninstallCliCommandTests
{
    [Fact]
    public void BuildReverseUninstallOrderFromImportConfig_ReversesGivenImportOrder()
    {
        var importOrder = new[] { "base", "mid", "top" };

        var order = DeployUninstallCliCommand.BuildReverseUninstallOrderFromImportConfig(importOrder);

        Assert.Equal(new[] { "top", "mid", "base" }, order);
    }

    [Fact]
    public void BuildReverseUninstallOrderFromImportConfig_DeduplicatesCaseInsensitive()
    {
        var importOrder = new[] { "Base", "Top", "base" };

        var order = DeployUninstallCliCommand.BuildReverseUninstallOrderFromImportConfig(importOrder);

        Assert.Equal(new[] { "Top", "Base" }, order);
    }
}
