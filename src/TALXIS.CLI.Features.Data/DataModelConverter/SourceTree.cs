using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Features.Data.DataModelConverter;

internal static class SourceTree
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// Yields the files matching a pattern beneath a root, without entering throwaway directories.
    /// A directory is entered once however many links lead to it, so a link back to an
    /// ancestor is not followed. A directory that cannot be listed, such as a dangling link,
    /// is skipped.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        root = Path.GetFullPath(root);
        var filter = new WorkspaceFileFilter(root, applyDefaults: true, readGitignore: false, skipNodeProjects: false);
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var pending = new Stack<(string Path, string RealPath)>();
        pending.Push((root, Path.TrimEndingDirectorySeparator(LinkTarget(new DirectoryInfo(root)) ?? root)));

        while (pending.Count > 0)
        {
            var (directory, realPath) = pending.Pop();

            if (!visited.Add(realPath))
            {
                continue;
            }

            string[] files;
            DirectoryInfo[] children;
            try
            {
                files = Directory.GetFiles(directory, pattern, Options);
                children = new DirectoryInfo(directory).GetDirectories("*", Options);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in children)
            {
                if (filter.IsIgnored(child.FullName))
                {
                    continue;
                }

                string? target;
                try
                {
                    target = LinkTarget(child);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                pending.Push((child.FullName, Path.TrimEndingDirectorySeparator(target ?? Path.Combine(realPath, child.Name))));
            }
        }
    }

    private static string? LinkTarget(DirectoryInfo directory) =>
        directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
            ? directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
            : null;
}
