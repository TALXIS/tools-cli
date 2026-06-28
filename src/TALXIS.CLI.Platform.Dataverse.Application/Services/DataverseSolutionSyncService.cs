using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionSyncService : ISolutionSyncService
{
    private readonly ISolutionPackagerService _packager;
    private readonly ILogger<DataverseSolutionSyncService> _logger;

    public DataverseSolutionSyncService(ISolutionPackagerService packager, ILogger<DataverseSolutionSyncService> logger)
    {
        _packager = packager;
        _logger = logger;
    }

    public async Task<SolutionSyncResult> SyncAsync(
        string? profileName,
        SolutionSyncOptions options,
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

            // Restore each assembly to the path convention already used in the destination
            // (matches by assembly simple name). New assemblies default to flat TALXIS SDK layout.
            var normalized = SolutionSyncTransform.RestoreLocalFileNameConventions(
                stagingRoot, options.SolutionRootPath, _logger);

            IReadOnlyList<string> excluded = [];
            IReadOnlyList<string> excludedWebResources = [];
            IReadOnlyList<string> excludedPcfControls = [];
            if (options.ProjectFilePath is not null)
            {
                var referencedAssemblies = ProjectReferenceReader.ReadPluginAssemblyNames(options.ProjectFilePath);
                excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(stagingRoot, referencedAssemblies);

                var scriptLibraryWebResources = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(
                    options.ProjectFilePath, _logger);
                excludedWebResources = SolutionSyncTransform.ExcludeScriptLibraryWebResources(stagingRoot, scriptLibraryWebResources);

                var pcfControls = ProjectReferenceReader.ReadPcfControlNames(options.ProjectFilePath);
                excludedPcfControls = SolutionSyncTransform.ExcludePcfControls(stagingRoot, pcfControls);
            }

            Directory.CreateDirectory(options.SolutionRootPath);
            var removed = SolutionSyncMerge.Merge(stagingRoot, options.SolutionRootPath);

            return new SolutionSyncResult(options.SolutionRootPath, normalized, excluded, excludedWebResources, excludedPcfControls, removed);
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
