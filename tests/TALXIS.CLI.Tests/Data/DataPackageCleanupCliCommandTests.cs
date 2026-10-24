using System.IO;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Data;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using Xunit;

namespace TALXIS.CLI.Tests.Data;

/// <summary>
/// Validates input-validation paths on <see cref="DataPackageCleanupCliCommand"/>
/// and the pure helpers it exposes. These tests deliberately stop before any
/// service call so they don't depend on a live Dataverse connection.
/// </summary>
public class DataPackageCleanupCliCommandTests
{
    private const int ExitValidationError = 2;

    [Fact]
    public async Task RunAsync_MissingPath_ReturnsValidationError()
    {
        var cmd = new DataPackageCleanupCliCommand { Data = "   ", Yes = true };

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }

    [Fact]
    public async Task RunAsync_NonExistentPath_ReturnsValidationError()
    {
        var cmd = new DataPackageCleanupCliCommand
        {
            Data = Path.Combine(Path.GetTempPath(), "txc-tests-cleanup-missing-" + Guid.NewGuid().ToString("N")),
            Yes = true,
        };

        var exit = await cmd.RunAsync();

        Assert.Equal(ExitValidationError, exit);
    }

    [Fact]
    public async Task RunAsync_BadBatchSize_ReturnsValidationError()
    {
        var tempFolder = CreateValidPackageFolder();
        try
        {
            var cmd = new DataPackageCleanupCliCommand
            {
                Data = tempFolder,
                Yes = true,
                BatchSize = 0,
            };

            var exit = await cmd.RunAsync();

            Assert.Equal(ExitValidationError, exit);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_BadConnectionCount_ReturnsValidationError()
    {
        var tempFolder = CreateValidPackageFolder();
        try
        {
            var cmd = new DataPackageCleanupCliCommand
            {
                Data = tempFolder,
                Yes = true,
                ConnectionCount = 0,
            };

            var exit = await cmd.RunAsync();

            Assert.Equal(ExitValidationError, exit);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_UnknownMissingAction_ReturnsValidationError()
    {
        var tempFolder = CreateValidPackageFolder();
        try
        {
            var cmd = new DataPackageCleanupCliCommand
            {
                Data = tempFolder,
                Yes = true,
                MissingAction = "magic",
            };

            var exit = await cmd.RunAsync();

            Assert.Equal(ExitValidationError, exit);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Theory]
    [InlineData("by-natural-key", DataPackageCleanupMissingAction.ByNaturalKey)]
    [InlineData("NATURAL-KEY", DataPackageCleanupMissingAction.ByNaturalKey)]
    [InlineData("skip", DataPackageCleanupMissingAction.Skip)]
    [InlineData("Skip", DataPackageCleanupMissingAction.Skip)]
    [InlineData("fail", DataPackageCleanupMissingAction.Fail)]
    [InlineData(null, DataPackageCleanupMissingAction.ByNaturalKey)]
    public void TryParseMissingAction_Accepts(string? value, DataPackageCleanupMissingAction expected)
    {
        var ok = DataPackageCleanupCliCommand.TryParseMissingAction(value, out var action);
        Assert.True(ok);
        Assert.Equal(expected, action);
    }

    [Fact]
    public void TryParseMissingAction_RejectsUnknownValues()
    {
        var ok = DataPackageCleanupCliCommand.TryParseMissingAction("???", out _);
        Assert.False(ok);
    }

    [Fact]
    public void BuildCleanupOrder_ReversesImportOrderAndAppendsExtraEntities()
    {
        var contents = new DataPackageContents(
            EntityImportOrder: new[] { "businessunit", "account", "contact" },
            Schemas: new Dictionary<string, DataPackageEntitySchema>(),
            Records: new Dictionary<string, IReadOnlyList<DataPackageRecordRow>>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = Array.Empty<DataPackageRecordRow>(),
                ["contact"] = Array.Empty<DataPackageRecordRow>(),
                ["lead"] = Array.Empty<DataPackageRecordRow>(),
            },
            M2mAssociations: Array.Empty<DataPackageM2mAssociation>());

        var order = DataverseDataPackageService.BuildCleanupOrder(contents);

        Assert.Equal(new[] { "contact", "account", "businessunit", "lead" }, order);
    }

    private static string CreateValidPackageFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "txc-tests-cleanup-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "data_schema.xml"), "<entities></entities>");
        File.WriteAllText(Path.Combine(folder, "data.xml"), "<entities></entities>");
        return folder;
    }
}
