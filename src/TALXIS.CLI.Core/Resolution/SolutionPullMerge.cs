namespace TALXIS.CLI.Core.Resolution;

public static class SolutionPullMerge
{
    // Mirrors each top-level folder of the export into the solution root. Top-level entries the
    // export doesn't contain (project file, bin, obj, ...) are left alone so they can't be deleted.
    public static IReadOnlyList<string> Merge(string fromRoot, string intoRoot)
    {
        var deleted = new List<string>();
        Directory.CreateDirectory(intoRoot);

        foreach (var file in Directory.GetFiles(fromRoot))
            File.Copy(file, Path.Combine(intoRoot, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(fromRoot))
            MirrorDirectory(dir, Path.Combine(intoRoot, Path.GetFileName(dir)), intoRoot, deleted);

        return deleted;
    }

    private static void MirrorDirectory(string from, string into, string intoRoot, List<string> deleted)
    {
        Directory.CreateDirectory(into);

        var fromFiles = new HashSet<string>(
            Directory.GetFiles(from).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var fromDirs = new HashSet<string>(
            Directory.GetDirectories(from).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(into, Path.GetFileName(file)), overwrite: true);

        foreach (var sub in Directory.GetDirectories(from))
            MirrorDirectory(sub, Path.Combine(into, Path.GetFileName(sub)), intoRoot, deleted);

        foreach (var file in Directory.GetFiles(into))
        {
            if (!fromFiles.Contains(Path.GetFileName(file)))
            {
                deleted.Add(Path.GetRelativePath(intoRoot, file));
                File.Delete(file);
            }
        }

        foreach (var sub in Directory.GetDirectories(into))
        {
            if (!fromDirs.Contains(Path.GetFileName(sub)))
            {
                foreach (var f in Directory.GetFiles(sub, "*", SearchOption.AllDirectories))
                    deleted.Add(Path.GetRelativePath(intoRoot, f));
                Directory.Delete(sub, recursive: true);
            }
        }
    }
}
