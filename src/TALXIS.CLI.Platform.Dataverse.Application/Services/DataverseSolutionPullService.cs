using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Pipeline;
using TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.Platform.Metadata.Packaging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionPullService : ISolutionPullService
{
    private readonly ISolutionPackagerService _packager;
    private readonly IProjectReferenceMetadataReader _projectReferenceReader;
    private readonly ILogger<DataverseSolutionPullService> _logger;

    public DataverseSolutionPullService(
        ISolutionPackagerService packager,
        IProjectReferenceMetadataReader projectReferenceReader,
        ILogger<DataverseSolutionPullService> logger)
    {
        _packager = packager;
        _projectReferenceReader = projectReferenceReader;
        _logger = logger;
    }

    public async Task<SolutionPullResult> PullAsync(
        string? profileName,
        SolutionPullOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var zipBytes = await SolutionExporter.ExportAsync(
            conn.Client, options.SolutionUniqueName, managed: false, ct).ConfigureAwait(false);

        var tempZip = Path.Combine(Path.GetTempPath(), $"txc_sync_{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"txc_sync_{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(tempZip, zipBytes, ct).ConfigureAwait(false);

            Directory.CreateDirectory(stagingRoot);
            _packager.Unpack(tempZip, stagingRoot, managed: false);

            var context = new SolutionPullContext
            {
                StagingDirectory = stagingRoot,
                DestinationDirectory = options.SolutionRootPath,
                ProjectFilePath = options.ProjectFilePath,
                ReferencedProjectDirectories = []
            };

            var steps = new ISolutionPullStep[]
            {
                new SystemRelationshipExclusionStep(),
                new SolutionManifestNormalizationStep(),
                new PluginAssemblyNormalizationStep(_logger),
                new ProjectReferenceBinaryExclusionStep(_projectReferenceReader),
                new ScriptLibraryExclusionStep(_projectReferenceReader, _logger),
                new PcfControlExclusionStep(_projectReferenceReader)
            };

            foreach (var step in steps)
                step.Execute(context);

            Directory.CreateDirectory(options.SolutionRootPath);
            var removed = SolutionPullMerge.Merge(context.StagingDirectory, context.DestinationDirectory);

            return new SolutionPullResult(
                options.SolutionRootPath,
                context.NormalizedAssemblies,
                context.ExcludedRelationships,
                context.ExcludedBinaries,
                context.ExcludedWebResources,
                context.ExcludedPcfControls,
                removed);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }
}
