using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionPullService : ISolutionPullService
{
    private readonly ISolutionPackagerService _packager;
    private readonly ILogger<DataverseSolutionPullService> _logger;

    public DataverseSolutionPullService(ISolutionPackagerService packager, ILogger<DataverseSolutionPullService> logger)
    {
        _packager = packager;
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

            var excludedRelationships = SolutionPullTransform.ExcludeStandardSystemRelationships(stagingRoot);
            SolutionPullTransform.NormalizeSolutionManifest(stagingRoot, options.SolutionRootPath);

            // Restore each assembly to the path convention already used in the destination
            // (matches by assembly simple name). New assemblies default to flat TALXIS SDK layout.
            var normalized = SolutionPullTransform.RestoreLocalFileNameConventions(
                stagingRoot, options.SolutionRootPath, _logger);

            IReadOnlyList<string> excluded = [];
            IReadOnlyList<string> excludedWebResources = [];
            IReadOnlyList<string> excludedPcfControls = [];
            if (options.ProjectFilePath is not null)
            {
                var referencedAssemblies = ProjectReferenceReader.ReadPluginAssemblyNames(options.ProjectFilePath);
                excluded = SolutionPullTransform.ExcludeProjectReferenceBinaries(stagingRoot, referencedAssemblies);

                var scriptLibraryWebResources = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(
                    options.ProjectFilePath, _logger);
                excludedWebResources = SolutionPullTransform.ExcludeScriptLibraryWebResources(stagingRoot, scriptLibraryWebResources);

                var pcfControls = ProjectReferenceReader.ReadPcfControlNames(options.ProjectFilePath);
                excludedPcfControls = SolutionPullTransform.ExcludePcfControls(stagingRoot, pcfControls);
            }

            Directory.CreateDirectory(options.SolutionRootPath);
            var removed = SolutionPullMerge.Merge(stagingRoot, options.SolutionRootPath);

            return new SolutionPullResult(
                options.SolutionRootPath,
                normalized,
                excludedRelationships,
                excluded,
                excludedWebResources,
                excludedPcfControls,
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
