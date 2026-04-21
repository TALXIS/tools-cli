using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Deploy;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy;

public class DeployUninstallCliCommandTests
{
    [Fact]
    public void BuildReverseUninstallOrder_ReversesImportOrderByStartTime()
    {
        var t0 = DateTime.UtcNow;
        var correlated = new[]
        {
            new SolutionHistoryRecord(Guid.NewGuid(), "base", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, t0, t0.AddMinutes(1), "Completed"),
            new SolutionHistoryRecord(Guid.NewGuid(), "mid", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, t0.AddMinutes(1), t0.AddMinutes(2), "Completed"),
            new SolutionHistoryRecord(Guid.NewGuid(), "top", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, t0.AddMinutes(2), t0.AddMinutes(3), "Completed"),
        };

        var order = DeployUninstallCliCommand.BuildReverseUninstallOrder(correlated);

        Assert.Equal(new[] { "top", "mid", "base" }, order);
    }

    [Fact]
    public void BuildReverseUninstallOrder_DeduplicatesBySolutionName_CaseInsensitive()
    {
        var t0 = DateTime.UtcNow;
        var correlated = new[]
        {
            new SolutionHistoryRecord(Guid.NewGuid(), "Base", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, t0, t0.AddMinutes(1), "Completed"),
            new SolutionHistoryRecord(Guid.NewGuid(), "base", "1.0.0.1", "pkg", 1, "Import", 2, "Upgrade", null, t0.AddMinutes(2), t0.AddMinutes(3), "Completed"),
            new SolutionHistoryRecord(Guid.NewGuid(), "Top", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, t0.AddMinutes(1), t0.AddMinutes(2), "Completed"),
        };

        var order = DeployUninstallCliCommand.BuildReverseUninstallOrder(correlated);

        Assert.Equal(new[] { "Top", "Base" }, order);
    }

    [Fact]
    public void BuildReverseUninstallOrder_UsesEncounterOrderWhenStartTimeMissing()
    {
        var correlated = new[]
        {
            new SolutionHistoryRecord(Guid.NewGuid(), "first", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, null, null, "Completed"),
            new SolutionHistoryRecord(Guid.NewGuid(), "second", "1.0.0.0", "pkg", 1, "Import", 1, "Install", null, null, null, "Completed"),
        };

        var order = DeployUninstallCliCommand.BuildReverseUninstallOrder(correlated);

        Assert.Equal(new[] { "second", "first" }, order);
    }

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
