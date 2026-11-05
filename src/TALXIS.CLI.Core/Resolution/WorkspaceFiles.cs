namespace TALXIS.CLI.Core.Resolution;

/// <summary>
/// Walks a workspace's source files, skipping build output, package folders
/// and hidden directories (<c>.git</c>, <c>.txc</c>), which never hold sources.
/// </summary>
public static class WorkspaceFiles
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", "TestResults",
    };

    /// <summary>Files under <paramref name="root"/> matching <paramref name="pattern"/>, read lazily.</summary>
    public static IEnumerable<string> Enumerate(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files, children;
            try
            {
                files = Directory.GetFiles(directory, pattern);
                children = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                // A directory the user cannot read holds nothing they could build either.
                continue;
            }
            catch (IOException)
            {
                // Removed while walking.
                continue;
            }

            foreach (var file in files)
                yield return file;

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (!name.StartsWith('.') && !SkippedDirectories.Contains(name))
                    pending.Push(child);
            }
        }
    }
}
