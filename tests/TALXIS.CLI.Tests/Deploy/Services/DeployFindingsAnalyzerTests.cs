using System;
using System.Collections.Generic;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Deploy;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy.Services;

public class DeployFindingsAnalyzerTests
{
    private const string OverwriteString = "Overwrite customizations was enabled — SmartDiff was skipped. Re-run without --force-overwrite to cut import time.";
    private const string InstallUpgradeString = "Run used install + upgrade pattern instead of a single-step upgrade. Use --stage-and-upgrade to keep the upgrade atomic.";
    private const string SmartDiffAbsentString = "SmartDiff did not apply on upgrade path. Check settings or component churn.";

    private static SolutionHistoryRecord MakeRecord(
        int? suboperation = 5,
        string solutionName = "MySolution",
        bool? overwrite = null,
        DateTime? start = null,
        DateTime? end = null,
        Guid? id = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            SolutionName: solutionName,
            SolutionVersion: null,
            PackageName: null,
            OperationCode: 1,
            OperationLabel: "Import",
            SuboperationCode: suboperation,
            SuboperationLabel: SolutionHistoryMappings.MapSuboperation(suboperation),
            OverwriteUnmanagedCustomizations: overwrite,
            StartedAtUtc: start,
            CompletedAtUtc: end,
            Result: "Success");

    [Fact]
    public void Overwrite_FiresOnUpgradeWithOverwriteFlagFromRecord()
    {
        var input = new DeployFindingsInput
        {
            Primary = MakeRecord(suboperation: 5, overwrite: true),
            Solutions = new[] { MakeRecord(suboperation: 5, overwrite: true) },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(OverwriteString, result);
    }

    [Fact]
    public void Overwrite_FiresOnUpdateManagedWithXmlEvidence()
    {
        const string xml = "<ImportExportXml overwriteunmanagedcustomizations=\"true\"><SolutionManifest Managed=\"1\"/></ImportExportXml>";
        var input = new DeployFindingsInput
        {
            ImportJobData = xml,
            Primary = MakeRecord(suboperation: 3),
            Solutions = new[] { MakeRecord(suboperation: 3) },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(OverwriteString, result);
    }

    [Fact]
    public void Overwrite_DoesNotFireOnInstall()
    {
        var input = new DeployFindingsInput
        {
            Primary = MakeRecord(suboperation: 1, overwrite: true),
            Solutions = new[] { MakeRecord(suboperation: 1, overwrite: true) },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(OverwriteString, result);
    }

    [Fact]
    public void InstallUpgrade_FiresWhenInstallAndUpgradeShareSolutionName()
    {
        var input = new DeployFindingsInput
        {
            Solutions = new[]
            {
                MakeRecord(suboperation: 1, solutionName: "my_sol"),
                MakeRecord(suboperation: 5, solutionName: "my_sol"),
            },
            IsPackageMode = true,
            IncludeSolutions = true,
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(InstallUpgradeString, result);
    }

    [Fact]
    public void InstallUpgrade_DoesNotFireForDifferentSolutions()
    {
        var input = new DeployFindingsInput
        {
            Solutions = new[]
            {
                MakeRecord(suboperation: 1, solutionName: "sol_a"),
                MakeRecord(suboperation: 5, solutionName: "sol_b"),
            },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(InstallUpgradeString, result);
    }

    [Fact]
    public void SmartDiffAbsent_FiresOnUpgradeWhenXmlHasNoIndicator()
    {
        const string xml = "<ImportExportXml><SolutionManifest/></ImportExportXml>";
        var input = new DeployFindingsInput
        {
            ImportJobData = xml,
            Primary = MakeRecord(suboperation: 5),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(SmartDiffAbsentString, result);
    }

    [Fact]
    public void SmartDiffAbsent_DoesNotFireWhenIndicatorPresent()
    {
        const string xml = "<ImportExportXml><SmartDiff applied=\"true\"/></ImportExportXml>";
        var input = new DeployFindingsInput
        {
            ImportJobData = xml,
            Primary = MakeRecord(suboperation: 5),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(SmartDiffAbsentString, result);
    }

    [Fact]
    public void SmartDiffAbsent_SuppressedWhenOverwriteFired()
    {
        const string xml = "<ImportExportXml overwriteunmanagedcustomizations=\"true\"><SolutionManifest Managed=\"1\"/></ImportExportXml>";
        var input = new DeployFindingsInput
        {
            ImportJobData = xml,
            Primary = MakeRecord(suboperation: 5, overwrite: true),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(OverwriteString, result);
        Assert.DoesNotContain(SmartDiffAbsentString, result);
    }

    [Fact]
    public void SlowestImports_FiresWithTopTwoByDuration()
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var input = new DeployFindingsInput
        {
            IsPackageMode = true,
            IncludeSolutions = true,
            Solutions = new[]
            {
                MakeRecord(solutionName: "fast",   suboperation: 1, start: start, end: start.AddSeconds(5)),
                MakeRecord(solutionName: "medium", suboperation: 1, start: start, end: start.AddSeconds(30)),
                MakeRecord(solutionName: "slow",   suboperation: 1, start: start, end: start.AddSeconds(60)),
            },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(result, f => f.StartsWith("Slowest imports: slow, medium"));
    }

    [Fact]
    public void SlowestImports_NotEmittedForSingleSolution()
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var input = new DeployFindingsInput
        {
            IsPackageMode = true,
            IncludeSolutions = true,
            Solutions = new[] { MakeRecord(solutionName: "only", suboperation: 1, start: start, end: start.AddSeconds(10)) },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.StartsWith("Slowest imports:"));
    }

    private const string StaleInProcessString = "Package history status is still 'In Process'";
    private const string InstallUpdateString = "One or more solutions were installed and then updated within the same deployment window";

    [Fact]
    public void StaleInProcess_FiresWhenPackageStatusIsInProcessAndOlderThan1Hour()
    {
        var input = new DeployFindingsInput
        {
            IsPackageMode = true,
            PackageStatus = "In Process",
            PackageStartedAtUtc = DateTime.UtcNow.AddHours(-2),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(result, f => f.Contains(StaleInProcessString));
    }

    [Fact]
    public void StaleInProcess_DoesNotFireWhenRecentPackageIsInProcess()
    {
        var input = new DeployFindingsInput
        {
            IsPackageMode = true,
            PackageStatus = "In Process",
            PackageStartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains(StaleInProcessString));
    }

    [Fact]
    public void StaleInProcess_DoesNotFireWhenStatusIsCompleted()
    {
        var input = new DeployFindingsInput
        {
            IsPackageMode = true,
            PackageStatus = "Completed",
            PackageStartedAtUtc = DateTime.UtcNow.AddHours(-2),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains(StaleInProcessString));
    }

    [Fact]
    public void StaleInProcess_DoesNotFireInSolutionMode()
    {
        var input = new DeployFindingsInput
        {
            IsPackageMode = false,
            PackageStatus = "In Process",
            PackageStartedAtUtc = DateTime.UtcNow.AddHours(-2),
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains(StaleInProcessString));
    }

    [Fact]
    public void InstallUpdate_FiresWhenInstallAndUpdateShareSolutionName()
    {
        var input = new DeployFindingsInput
        {
            Solutions = new[]
            {
                MakeRecord(suboperation: 1, solutionName: "my_sol"),
                MakeRecord(suboperation: 3, solutionName: "my_sol"),
            },
            IsPackageMode = true,
            IncludeSolutions = true,
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(result, f => f.Contains(InstallUpdateString));
    }

    [Fact]
    public void InstallUpdate_DoesNotFireForDifferentSolutions()
    {
        var input = new DeployFindingsInput
        {
            Solutions = new[]
            {
                MakeRecord(suboperation: 1, solutionName: "sol_a"),
                MakeRecord(suboperation: 3, solutionName: "sol_b"),
            },
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains(InstallUpdateString));
    }

    [Fact]
    public void InstallUpgrade_AndInstallUpdate_CanBothFireInSameRun()
    {
        var input = new DeployFindingsInput
        {
            Solutions = new[]
            {
                MakeRecord(suboperation: 1, solutionName: "sol_holding"),
                MakeRecord(suboperation: 5, solutionName: "sol_holding"),
                MakeRecord(suboperation: 1, solutionName: "sol_double"),
                MakeRecord(suboperation: 3, solutionName: "sol_double"),
            },
            IsPackageMode = true,
            IncludeSolutions = true,
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(result, f => f.Contains("single-step upgrade"));
        Assert.Contains(result, f => f.Contains(InstallUpdateString));
    }

    [Fact]
    public void NoSolutionsDetected_FiresWhenPackageCompletedWithNoSolutions()
    {
        var input = new DeployFindingsInput
        {
            Solutions = Array.Empty<SolutionHistoryRecord>(),
            IsPackageMode = true,
            IncludeSolutions = true,
            PackageStatus = "Completed",
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.Contains(result, f => f.Contains("already at the required version"));
    }

    [Fact]
    public void NoSolutionsDetected_DoesNotFireWhenPackageIsInProcess()
    {
        var input = new DeployFindingsInput
        {
            Solutions = Array.Empty<SolutionHistoryRecord>(),
            IsPackageMode = true,
            IncludeSolutions = true,
            PackageStatus = "In Process",
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains("already at the required version"));
    }

    [Fact]
    public void NoSolutionsDetected_DoesNotFireWhenSolutionsArePresent()
    {
        var input = new DeployFindingsInput
        {
            Solutions = new[] { MakeRecord() },
            IsPackageMode = true,
            IncludeSolutions = true,
            PackageStatus = "Completed",
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains("already at the required version"));
    }

    [Fact]
    public void NoSolutionsDetected_DoesNotFireInSolutionMode()
    {
        var input = new DeployFindingsInput
        {
            Solutions = Array.Empty<SolutionHistoryRecord>(),
            IsPackageMode = false,
            IncludeSolutions = false,
            PackageStatus = "Completed",
        };

        var result = DeployFindingsAnalyzer.Analyze(input);

        Assert.DoesNotContain(result, f => f.Contains("already at the required version"));
    }
}
