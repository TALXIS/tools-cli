using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Resolution;

/// <summary>
/// Walks from <c>startDirectory</c> up to the filesystem root looking for
/// <c>.txc/workspace.json</c>. First hit wins.
/// </summary>
public sealed class WorkspaceDiscovery : IWorkspaceDiscovery
{
    public const string DirectoryName = ".txc";
    public const string FileName = "workspace.json";

    public async Task<WorkspaceResolution?> DiscoverAsync(string startDirectory, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)) return null;

        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (dir is not null)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = Path.Combine(dir.FullName, DirectoryName, FileName);
            if (File.Exists(candidate))
            {
                var config = await JsonFile.ReadOrDefaultAsync<WorkspaceConfig>(candidate, ct).ConfigureAwait(false);
                return new WorkspaceResolution(dir.FullName, candidate, config);
            }
            dir = dir.Parent;
        }
        return null;
    }
}
