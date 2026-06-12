using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionSyncService : ISolutionSyncService
{
    private readonly ISolutionPackagerService _packager;

    public DataverseSolutionSyncService(ISolutionPackagerService packager)
    {
        _packager = packager;
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

            var normalized = SolutionSyncTransform.NormalizePluginAssemblyPaths(stagingRoot);

            IReadOnlyList<string> excluded = [];
            IReadOnlyList<string> excludedWebResources = [];
            if (options.ProjectFilePath is not null)
            {
                var referencedAssemblies = ProjectReferenceReader.ReadPluginAssemblyNames(options.ProjectFilePath);
                excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(stagingRoot, referencedAssemblies);

                var scriptLibraryWebResources = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(options.ProjectFilePath);
                excludedWebResources = SolutionSyncTransform.ExcludeScriptLibraryWebResources(stagingRoot, scriptLibraryWebResources);
            }

            Directory.CreateDirectory(options.SolutionRootPath);
            var removed = SolutionSyncMerge.Merge(stagingRoot, options.SolutionRootPath);

            return new SolutionSyncResult(options.SolutionRootPath, normalized, excluded, excludedWebResources, removed);
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
